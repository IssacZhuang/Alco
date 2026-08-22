using Alco.Graphics;

namespace Alco.Graphics.Spirv;

/// <summary>
/// Rewrites SPIR-V modules so that selected sampled textures are declared
/// as depth images (<c>OpTypeImage</c> Depth operand = 1) instead of "unknown" (2).
/// <br/>Some shader compiler paths emit the Depth operand as 2 (unknown). WebGPU (wgpu/naga)
/// only recognizes a depth texture when the operand is exactly 1, otherwise the texture
/// is treated as a regular float texture and fails pipeline validation when a real depth
/// texture is bound. This patcher clones the image type chain of each requested texture
/// variable so only that variable becomes a depth image; other textures sharing the same
/// SPIR-V type declaration keep the original non-depth type.
/// <br/>Cloned types are reused across textures sharing a declaration, since duplicate
/// non-aggregate type declarations are invalid in SPIR-V.
/// </summary>
public static class SpirvDepthTexturePatcher
{
    /// <summary>
    /// Specialized indexes built on top of <see cref="SpirvModule"/> for the patcher's
    /// type-chain rewriting: reverse lookups for load/sampled-image/image instructions.
    /// </summary>
    private sealed class PatcherIndex
    {
        public required SpirvModule Module;
        public Dictionary<uint, int> Variables { get; } = new();
        public Dictionary<uint, int> TypeImages { get; } = new();
        public Dictionary<uint, int> TypePointers { get; } = new();
        public Dictionary<uint, int> TypeSampledImages { get; } = new();
        public Dictionary<uint, List<int>> LoadsByPointer { get; } = new();
        public Dictionary<uint, List<int>> SampledImagesByImage { get; } = new();
        public Dictionary<uint, List<int>> ImagesBySampledImage { get; } = new();
    }

    /// <summary>
    /// Per-module rewrite state. Cloned types are cached so several depth textures sharing
    /// one declaration reuse the same clone: duplicate non-aggregate type declarations
    /// are rejected by SPIR-V validators (spirv-val).
    /// </summary>
    private sealed class PatchContext
    {
        public Dictionary<uint, uint> ImageTypeMap { get; } = new();
        public Dictionary<(uint storageClass, uint pointee), uint> PointerTypeMap { get; } = new();
        public Dictionary<uint, uint> SampledImageTypeMap { get; } = new();
        public Dictionary<(int instruction, int word), uint> WordPatches { get; } = new();
        public Dictionary<int, List<SpirvInstruction>> InsertAfter { get; } = new();
    }

    /// <summary>
    /// Mark the given global texture variables as depth images in the SPIR-V module.
    /// Names not present in the module are silently ignored (per-stage modules may not
    /// reference every texture).
    /// </summary>
    public static byte[] MarkDepthTexturesByName(
        byte[] spirv,
        IReadOnlyCollection<string> textureNames)
    {
        if (textureNames.Count == 0)
        {
            return spirv;
        }

        SpirvModule module = SpirvReader.Parse(spirv);
        HashSet<string> requestedNames = new(textureNames);
        Dictionary<uint, string> selectedVariables = new();
        foreach (KeyValuePair<uint, string> pair in module.Names)
        {
            if (requestedNames.Contains(pair.Value))
            {
                selectedVariables[pair.Key] = pair.Value;
            }
        }

        return PatchSelectedVariables(spirv, module, selectedVariables);
    }

    /// <summary>
    /// Mark global texture variables at the given descriptor locations as depth images.
    /// Descriptor locations not present in the module are silently ignored.
    /// </summary>
    public static byte[] MarkDepthTexturesByBinding(
        byte[] spirv,
        IReadOnlyDictionary<(uint Set, uint Binding), string> textureBindings)
    {
        if (textureBindings.Count == 0)
        {
            return spirv;
        }

        SpirvModule module = SpirvReader.Parse(spirv);
        Dictionary<uint, string> selectedVariables = new();
        foreach (SpirvInstruction instruction in module.Instructions)
        {
            if ((SpirvOp)instruction.OpCode != SpirvOp.Variable)
            {
                continue;
            }

            uint variableId = instruction[2];
            if (!module.HasDecoration(variableId, SpirvDecoration.DescriptorSet) ||
                !module.HasDecoration(variableId, SpirvDecoration.Binding))
            {
                continue;
            }

            var descriptor = (
                Set: module.GetDecorationValue(variableId, SpirvDecoration.DescriptorSet),
                Binding: module.GetDecorationValue(variableId, SpirvDecoration.Binding));
            if (textureBindings.TryGetValue(descriptor, out string? name))
            {
                selectedVariables[variableId] = name;
            }
        }

        return PatchSelectedVariables(spirv, module, selectedVariables);
    }

    private static byte[] PatchSelectedVariables(
        byte[] spirv,
        SpirvModule module,
        IReadOnlyDictionary<uint, string> selectedVariables)
    {
        PatcherIndex index = BuildIndex(module);
        PatchContext context = new();
        uint bound = module.Bound;
        foreach (KeyValuePair<uint, string> pair in selectedVariables)
        {
            if (index.Variables.TryGetValue(pair.Key, out int variableIndex))
            {
                bound = PatchVariable(index, context, variableIndex, pair.Value, bound);
            }
        }

        if (context.WordPatches.Count == 0)
        {
            return spirv;
        }

        foreach (KeyValuePair<(int instruction, int word), uint> patch in context.WordPatches)
        {
            module.Instructions[patch.Key.instruction][patch.Key.word] = patch.Value;
        }

        module.Bound = Math.Max(bound, module.Bound);
        return module.ToBytes(context.InsertAfter);
    }

    private static uint PatchVariable(
        PatcherIndex index, PatchContext context, int variableIndex, string name, uint bound)
    {
        SpirvModule module = index.Module;
        SpirvInstruction variable = module.Instructions[variableIndex];
        uint variableId = variable[2];
        uint pointerTypeId = variable[1];

        if (!index.TypePointers.TryGetValue(pointerTypeId, out int pointerIndex))
        {
            throw new ShaderReflectionException(
                $"Cannot mark depth texture '{name}': its variable does not reference an OpTypePointer.");
        }

        SpirvInstruction pointer = module.Instructions[pointerIndex];
        uint imageTypeId = pointer[3];
        if (!index.TypeImages.TryGetValue(imageTypeId, out int imageIndex))
        {
            throw new ShaderReflectionException(
                $"Cannot mark depth texture '{name}': its variable is not a sampled image (OpTypeImage).");
        }

        SpirvInstruction image = module.Instructions[imageIndex];
        if (image[4] == 1)
        {
            return bound; // Already a depth image.
        }

        // Clone the image type with Depth = 1, or reuse an existing clone.
        if (!context.ImageTypeMap.TryGetValue(imageTypeId, out uint newImageTypeId))
        {
            newImageTypeId = bound++;
            SpirvInstruction newImage = image.Clone();
            newImage[1] = newImageTypeId;
            newImage[4] = 1;
            AddInsertion(context.InsertAfter, imageIndex, newImage);
            context.ImageTypeMap.Add(imageTypeId, newImageTypeId);
        }

        // Find or clone the pointer type referencing the depth image clone.
        (uint storageClass, uint pointee) pointerKey = (pointer[2], newImageTypeId);
        if (!context.PointerTypeMap.TryGetValue(pointerKey, out uint newPointerTypeId))
        {
            newPointerTypeId = FindPointerType(index, pointer[2], newImageTypeId);
            if (newPointerTypeId == 0)
            {
                newPointerTypeId = bound++;
                SpirvInstruction newPointer = pointer.Clone();
                newPointer[1] = newPointerTypeId;
                newPointer[3] = newImageTypeId;
                AddInsertion(context.InsertAfter, pointerIndex, newPointer);
            }

            context.PointerTypeMap.Add(pointerKey, newPointerTypeId);
        }

        // Repoint the variable at the depth pointer type.
        context.WordPatches[(variableIndex, 1)] = newPointerTypeId;

        // Rewire every load of the variable to the depth image type.
        HashSet<uint> loadedImageIds = new();
        if (index.LoadsByPointer.TryGetValue(variableId, out List<int>? loadIndices))
        {
            foreach (int loadIndex in loadIndices)
            {
                SpirvInstruction load = module.Instructions[loadIndex];
                context.WordPatches[(loadIndex, 1)] = newImageTypeId;
                loadedImageIds.Add(load[2]);
            }
        }

        // Clone OpTypeSampledImage chains wrapping those loads.
        HashSet<uint> sampledImageIds = new();
        foreach (uint loadedImageId in loadedImageIds)
        {
            if (!index.SampledImagesByImage.TryGetValue(loadedImageId, out List<int>? sampledIndices))
            {
                continue;
            }

            foreach (int sampledIndex in sampledIndices)
            {
                SpirvInstruction sampledImage = module.Instructions[sampledIndex];
                uint sampledImageTypeId = sampledImage[1];

                if (!context.SampledImageTypeMap.TryGetValue(sampledImageTypeId, out uint newSampledImageTypeId))
                {
                    if (!index.TypeSampledImages.TryGetValue(sampledImageTypeId, out int typeSampledImageIndex))
                    {
                        throw new ShaderReflectionException(
                            $"Cannot mark depth texture '{name}': OpSampledImage references an unknown type.");
                    }

                    newSampledImageTypeId = bound++;
                    SpirvInstruction newTypeSampledImage = module.Instructions[typeSampledImageIndex].Clone();
                    newTypeSampledImage[1] = newSampledImageTypeId;
                    newTypeSampledImage[2] = newImageTypeId;
                    AddInsertion(context.InsertAfter, typeSampledImageIndex, newTypeSampledImage);
                    context.SampledImageTypeMap.Add(sampledImageTypeId, newSampledImageTypeId);
                }

                context.WordPatches[(sampledIndex, 1)] = newSampledImageTypeId;
                sampledImageIds.Add(sampledImage[2]);
            }
        }

        // Rewire OpImage (extract image from sampled image) results to the depth image type.
        foreach (uint sampledImageId in sampledImageIds)
        {
            if (!index.ImagesBySampledImage.TryGetValue(sampledImageId, out List<int>? imageIndices))
            {
                continue;
            }

            foreach (int imageInstructionIndex in imageIndices)
            {
                context.WordPatches[(imageInstructionIndex, 1)] = newImageTypeId;
            }
        }

        return bound;
    }

    private static PatcherIndex BuildIndex(SpirvModule module)
    {
        PatcherIndex index = new() { Module = module };

        for (int i = 0; i < module.Instructions.Count; i++)
        {
            SpirvInstruction inst = module.Instructions[i];
            switch ((SpirvOp)inst.OpCode)
            {
                case SpirvOp.Variable:
                    index.Variables[inst[2]] = i;
                    break;
                case SpirvOp.TypeImage:
                    index.TypeImages[inst[1]] = i;
                    break;
                case SpirvOp.TypePointer:
                    index.TypePointers[inst[1]] = i;
                    break;
                case SpirvOp.TypeSampledImage:
                    index.TypeSampledImages[inst[1]] = i;
                    break;
                case SpirvOp.Load:
                    AddUsage(index.LoadsByPointer, inst[3], i);
                    break;
                case SpirvOp.SampledImage:
                    AddUsage(index.SampledImagesByImage, inst[3], i);
                    break;
                case SpirvOp.Image:
                    AddUsage(index.ImagesBySampledImage, inst[3], i);
                    break;
            }
        }

        return index;
    }

    private static uint FindPointerType(PatcherIndex index, uint storageClass, uint pointeeTypeId)
    {
        SpirvModule module = index.Module;
        foreach (KeyValuePair<uint, int> pair in index.TypePointers)
        {
            SpirvInstruction pointer = module.Instructions[pair.Value];
            if (pointer[2] == storageClass && pointer[3] == pointeeTypeId)
            {
                return pair.Key;
            }
        }

        return 0;
    }

    private static void AddUsage(Dictionary<uint, List<int>> usages, uint operandId, int instructionIndex)
    {
        if (!usages.TryGetValue(operandId, out List<int>? list))
        {
            list = new List<int>();
            usages.Add(operandId, list);
        }

        list.Add(instructionIndex);
    }

    private static void AddInsertion(Dictionary<int, List<SpirvInstruction>> insertAfter, int instructionIndex, SpirvInstruction instruction)
    {
        if (!insertAfter.TryGetValue(instructionIndex, out List<SpirvInstruction>? list))
        {
            list = new List<SpirvInstruction>();
            insertAfter.Add(instructionIndex, list);
        }

        list.Add(instruction);
    }
}
