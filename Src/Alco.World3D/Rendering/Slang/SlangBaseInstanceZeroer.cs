using Alco.Graphics.Spirv;

namespace Alco.World3D;

/// <summary>
/// SPIR-V post-processing for the Slang pipeline path. Slang maps
/// <c>SV_InstanceID</c> to <c>gl_InstanceIndex - gl_BaseInstance</c> but does
/// not emit the <c>DrawParameters</c> capability that the BaseInstance builtin
/// requires in Vulkan, which wgpu rejects when the shader module is created.
/// The engine's DXC path compiles without
/// <c>-fvk-support-nonzero-base-instance</c>, so <c>SV_InstanceID</c> is
/// <c>gl_InstanceIndex</c> there. World3D deliberately uses the draw's first
/// instance as an offset into its instance buffer, so replacing the Slang
/// module's <c>gl_BaseInstance</c> load with zero preserves that offset and
/// reproduces the DXC behavior exactly. The variable, its builtin decoration,
/// and its entry-point interface entry are removed along with the rewired loads.
/// </summary>
internal static class SlangBaseInstanceZeroer
{
    private const uint BuiltInDecoration = 11;
    private const uint BaseInstanceBuiltin = 4425;
    private const uint InputStorageClass = 1;
    private const uint CopyObjectOpCode = 83;

    /// <summary>
    /// Replace loads of the BaseInstance builtin with zero and remove the
    /// builtin variable. Modules that do not reference it are returned as-is.
    /// </summary>
    public static byte[] ZeroBaseInstance(byte[] spirv)
    {
        SpirvModule module = SpirvReader.Parse(spirv);

        HashSet<uint> variableIds = [];
        foreach (SpirvInstruction inst in module.Instructions)
        {
            if (inst.OpCode == (ushort)SpirvOp.Decorate &&
                inst[2] == BuiltInDecoration &&
                inst[3] == BaseInstanceBuiltin)
            {
                variableIds.Add(inst[1]);
            }
        }

        if (variableIds.Count == 0)
        {
            return spirv;
        }

        // Loads of the builtin become copy-of-zero; resolve the variables'
        // pointee int type so a zero constant can be found or synthesized.
        HashSet<uint> pointerTypeIds = [];
        foreach (SpirvInstruction inst in module.Instructions)
        {
            if (inst.OpCode == (ushort)SpirvOp.Variable &&
                variableIds.Contains(inst[2]) &&
                inst[3] == InputStorageClass)
            {
                pointerTypeIds.Add(inst[1]);
            }
        }

        HashSet<uint> intTypeIds = [];
        foreach (SpirvInstruction inst in module.Instructions)
        {
            if (inst.OpCode == (ushort)SpirvOp.TypePointer &&
                pointerTypeIds.Contains(inst[1]))
            {
                intTypeIds.Add(inst[3]);
            }
        }

        if (intTypeIds.Count == 0)
        {
            return spirv; // Not an integer builtin; leave the module untouched.
        }

        uint zeroConstantId = FindOrCreateZeroConstant(module, intTypeIds, out uint intTypeId, out int insertAfter);

        // Rewrite every OpLoad of the builtin into OpCopyObject of the zero
        // constant (identical word layout, so the rewrite stays in place).
        foreach (SpirvInstruction inst in module.Instructions)
        {
            if (inst.OpCode == (ushort)SpirvOp.Load &&
                variableIds.Contains(inst[3]) &&
                inst.WordCount == 4)
            {
                inst[0] = (4u << 16) | CopyObjectOpCode;
                inst[3] = zeroConstantId;
            }
        }

        // Drop the variables, their decorations and names.
        for (int i = module.Instructions.Count - 1; i >= 0; i--)
        {
            SpirvInstruction inst = module.Instructions[i];
            bool remove = inst.OpCode switch
            {
                (ushort)SpirvOp.Variable => variableIds.Contains(inst[2]),
                (ushort)SpirvOp.Decorate => variableIds.Contains(inst[1]),
                (ushort)SpirvOp.Name => variableIds.Contains(inst[1]),
                _ => false,
            };
            if (remove)
            {
                module.Instructions.RemoveAt(i);
                if (insertAfter > i)
                {
                    insertAfter--;
                }
            }
        }

        // Remove the deleted variables from the entry-point interface list.
        for (int i = 0; i < module.Instructions.Count; i++)
        {
            if (module.Instructions[i].OpCode == (ushort)SpirvOp.EntryPoint)
            {
                SpirvInstruction? stripped = StripInterfaceEntry(module.Instructions[i], variableIds);
                if (stripped != null)
                {
                    module.Instructions[i] = stripped;
                }
            }
        }

        Dictionary<int, List<SpirvInstruction>>? insertions = null;
        if (zeroConstantId >= module.Bound)
        {
            module.Bound = zeroConstantId + 1;
            SpirvInstruction zero =
                new([(4u << 16) | (uint)SpirvOp.Constant, intTypeId, zeroConstantId, 0]);
            insertions = new() { [insertAfter] = [zero] };
        }

        return module.ToBytes(insertions);
    }

    /// <summary>
    /// Find an <c>OpConstant</c> of an int type with value zero, or prepare a
    /// synthesized one. <paramref name="insertAfter"/> is the instruction
    /// index to insert a synthesized constant after (-1 when one exists).
    /// </summary>
    private static uint FindOrCreateZeroConstant(
        SpirvModule module, HashSet<uint> intTypeIds, out uint intTypeId, out int insertAfter)
    {
        int typeIndex = -1;
        intTypeId = 0;
        for (int i = 0; i < module.Instructions.Count; i++)
        {
            SpirvInstruction inst = module.Instructions[i];
            if (inst.OpCode == (ushort)SpirvOp.TypeInt && intTypeIds.Contains(inst[1]))
            {
                if (typeIndex < 0)
                {
                    typeIndex = i;
                    intTypeId = inst[1];
                }
            }
            else if (inst.OpCode == (ushort)SpirvOp.Constant &&
                     intTypeIds.Contains(inst[1]) &&
                     inst.WordCount == 4 &&
                     inst[3] == 0)
            {
                insertAfter = -1;
                return inst[2];
            }
        }

        insertAfter = typeIndex;
        return module.Bound;
    }

    /// <summary>
    /// Rebuild an <see cref="SpirvOp.EntryPoint"/> without the removed
    /// interface ids, or return null when the interface does not reference
    /// them. Operands are: execution model, entry point id, NUL-terminated
    /// name literal, then the interface ids.
    /// </summary>
    private static SpirvInstruction? StripInterfaceEntry(SpirvInstruction entryPoint, HashSet<uint> removedIds)
    {
        int wordCount = entryPoint.WordCount;
        int interfaceStart = 3;
        while (interfaceStart < wordCount && !ContainsNulByte(entryPoint[interfaceStart]))
        {
            interfaceStart++;
        }

        if (interfaceStart >= wordCount)
        {
            return null; // Malformed name literal; leave the instruction alone.
        }

        List<uint> words = [];
        for (int i = 0; i <= interfaceStart; i++)
        {
            words.Add(entryPoint[i]);
        }

        bool changed = false;
        for (int i = interfaceStart + 1; i < wordCount; i++)
        {
            if (removedIds.Contains(entryPoint[i]))
            {
                changed = true;
                continue;
            }

            words.Add(entryPoint[i]);
        }

        if (!changed)
        {
            return null;
        }

        words[0] = ((uint)words.Count << 16) | (entryPoint[0] & 0xFFFF);
        return new SpirvInstruction([.. words]);
    }

    private static bool ContainsNulByte(uint word)
    {
        return (word & 0xFF) == 0 ||
            (word & 0xFF00) == 0 ||
            (word & 0xFF0000) == 0 ||
            (word & 0xFF000000) == 0;
    }
}
