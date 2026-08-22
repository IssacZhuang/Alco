using Alco.Graphics;
using Alco.Graphics.Spirv;
using Alco.Rendering;

namespace Alco.World3D;

/// <summary>
/// SPIR-V post-processing for the Slang pipeline path, mirroring what the engine's
/// DXC toolchain does after compiling the same HLSL sources (the engine's own
/// patcher is internal to Alco.ShaderCompiler, so this is a port built on the
/// public <c>Alco.Graphics.Spirv</c> module reader/writer).
/// <br/>Slang, like DXC, emits every <c>OpTypeImage</c> with the Depth operand
/// "unknown" (2), but wgpu/naga only accept a depth-texture binding when the
/// operand is exactly 1. This patcher clones the image type chain of each named
/// texture variable so only that variable becomes a depth image; textures
/// sharing the same SPIR-V type declaration keep the non-depth type. Variables
/// are selected by descriptor set and binding from Slang reflection instead of
/// <c>OpName</c>; the GLSL/glslang SPIR-V path is allowed to rename symbols.
/// Cloned types are cached per declaration because duplicate non-aggregate
/// type declarations are invalid in SPIR-V.
/// </summary>
internal static class SlangDepthTexturePatcher
{
    /// <summary>
    /// Mark the given global texture bindings as depth images in the SPIR-V
    /// module. Bindings not present in the module are silently ignored
    /// (per-stage modules may not reference every texture).
    /// </summary>
    public static byte[] MarkDepthTextures(
        byte[] spirv,
        IReadOnlyDictionary<(uint Set, uint Binding), string> textureBindings)
    {
        if (textureBindings.Count == 0)
        {
            return spirv;
        }

        SpirvModule module;
        try
        {
            module = SpirvReader.Parse(spirv);
        }
        catch (Exception ex) when (ex is ShaderReflectionException or ShaderCompilationException)
        {
            throw new ShaderValidationException($"Slang produced a SPIR-V module that cannot be parsed: {ex.Message}");
        }

        PatcherIndex index = BuildIndex(module);
        PatchContext context = new();
        uint bound = module.Bound;

        foreach (KeyValuePair<uint, int> pair in index.Variables)
        {
            uint variableId = pair.Key;
            if (!module.HasDecoration(variableId, SpirvDecoration.DescriptorSet) ||
                !module.HasDecoration(variableId, SpirvDecoration.Binding))
            {
                continue;
            }

            var descriptor = (
                Set: module.GetDecorationValue(variableId, SpirvDecoration.DescriptorSet),
                Binding: module.GetDecorationValue(variableId, SpirvDecoration.Binding));
            if (!textureBindings.TryGetValue(descriptor, out string? name))
            {
                continue;
            }

            bound = PatchVariable(index, context, pair.Value, name, bound);
        }

        if (context.WordPatches.Count == 0)
        {
            return spirv;
        }

        foreach (KeyValuePair<(int Instruction, int Word), uint> patch in context.WordPatches)
        {
            module.Instructions[patch.Key.Instruction][patch.Key.Word] = patch.Value;
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
            throw new ShaderValidationException(
                $"Cannot mark depth texture '{name}': its variable does not reference an OpTypePointer.");
        }

        SpirvInstruction pointer = module.Instructions[pointerIndex];
        uint imageTypeId = pointer[3];
        if (!index.TypeImages.TryGetValue(imageTypeId, out int imageIndex))
        {
            throw new ShaderValidationException(
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
        (uint StorageClass, uint Pointee) pointerKey = (pointer[2], newImageTypeId);
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
        HashSet<uint> loadedImageIds = [];
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
        HashSet<uint> sampledImageIds = [];
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
                        throw new ShaderValidationException(
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

    /// <summary>Reverse lookups for the patcher's type-chain rewriting.</summary>
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

    /// <summary>Per-module rewrite state with cloned-type caches.</summary>
    private sealed class PatchContext
    {
        public Dictionary<uint, uint> ImageTypeMap { get; } = new();
        public Dictionary<(uint StorageClass, uint Pointee), uint> PointerTypeMap { get; } = new();
        public Dictionary<uint, uint> SampledImageTypeMap { get; } = new();
        public Dictionary<(int Instruction, int Word), uint> WordPatches { get; } = new();
        public Dictionary<int, List<SpirvInstruction>> InsertAfter { get; } = new();
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
            list = [];
            usages.Add(operandId, list);
        }

        list.Add(instructionIndex);
    }

    private static void AddInsertion(Dictionary<int, List<SpirvInstruction>> insertAfter, int instructionIndex, SpirvInstruction instruction)
    {
        if (!insertAfter.TryGetValue(instructionIndex, out List<SpirvInstruction>? list))
        {
            list = [];
            insertAfter.Add(instructionIndex, list);
        }

        list.Add(instruction);
    }
}
