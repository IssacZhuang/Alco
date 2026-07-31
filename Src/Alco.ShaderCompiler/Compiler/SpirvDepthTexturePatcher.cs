using System.Text;

namespace Alco.ShaderCompiler;

/// <summary>
/// Rewrites SPIR-V modules produced by DXC so that selected sampled textures are declared
/// as depth images (<c>OpTypeImage</c> Depth operand = 1) instead of "unknown" (2).
/// <br/>DXC emits the Depth operand as 2 (unknown) for every HLSL texture type and offers
/// no attribute to change it (microsoft/DirectXShaderCompiler#5241). WebGPU (wgpu/naga)
/// only recognizes a depth texture when the operand is exactly 1, otherwise the texture
/// is treated as a regular float texture and fails pipeline validation when a real depth
/// texture is bound. This patcher clones the image type chain of each requested texture
/// variable so only that variable becomes a depth image; other textures sharing the same
/// SPIR-V type declaration keep the original non-depth type.
/// <br/>Cloned types are reused across textures sharing a declaration, since duplicate
/// non-aggregate type declarations are invalid in SPIR-V.
/// <br/>Supported usage pattern: global <c>Texture2D&lt;float&gt;</c> variables read via
/// <c>Load</c> (OpImageFetch) and/or <c>Sample</c> (OpImageSample*), which is what DXC
/// produces after inlining at -O3.
/// </summary>
internal static class SpirvDepthTexturePatcher
{
    private const uint MagicNumber = 0x07230203;
    private const int HeaderWordCount = 5;
    private const int BoundWordIndex = 3;

    // Opcodes used by the rewriter (stable across SPIR-V versions).
    private const ushort OpName = 5;
    private const ushort OpTypeImage = 25;
    private const ushort OpTypeSampledImage = 27;
    private const ushort OpTypePointer = 32;
    private const ushort OpVariable = 59;
    private const ushort OpLoad = 61;
    private const ushort OpSampledImage = 86;
    private const ushort OpImage = 100;

    private sealed class Module
    {
        public required uint[] Header;
        public required List<uint[]> Instructions;

        public Dictionary<uint, string> Names { get; } = new();
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
        /// <summary>Original OpTypeImage id to its depth (Depth = 1) clone id.</summary>
        public Dictionary<uint, uint> ImageTypeMap { get; } = new();

        /// <summary>(Storage class, pointee type id) of a pointer type to its (possibly cloned) type id.</summary>
        public Dictionary<(uint storageClass, uint pointee), uint> PointerTypeMap { get; } = new();

        /// <summary>Original OpTypeSampledImage id to its clone id (wrapping the depth image clone).</summary>
        public Dictionary<uint, uint> SampledImageTypeMap { get; } = new();

        /// <summary>Word mutations keyed by (instruction index, word index) into the original stream.</summary>
        public Dictionary<(int instruction, int word), uint> WordPatches { get; } = new();

        /// <summary>Cloned instructions to emit right after the instruction at the key index.</summary>
        public Dictionary<int, List<uint[]>> InsertAfter { get; } = new();
    }

    /// <summary>
    /// Mark the given global texture variables as depth images in the SPIR-V module.
    /// Names that are not present in the module are ignored, so the same name list can be
    /// applied to every stage module of a shader (an unused texture may be eliminated in
    /// stages that do not reference it).
    /// </summary>
    /// <param name="spirv">The SPIR-V bytecode produced by DXC.</param>
    /// <param name="textureNames">The HLSL global variable names of the depth textures.</param>
    /// <returns>The rewritten SPIR-V bytecode.</returns>
    /// <exception cref="ShaderCompilationException">
    /// Thrown when the module is malformed or a texture is used in a way this rewriter
    /// does not support (anything other than plain loads and sampling).
    /// </exception>
    public static byte[] MarkDepthTextures(byte[] spirv, IReadOnlyCollection<string> textureNames)
    {
        if (textureNames.Count == 0)
        {
            return spirv;
        }

        Module module = Parse(spirv);
        PatchContext context = new();
        uint bound = module.Header[BoundWordIndex];

        foreach (string name in textureNames.Distinct())
        {
            foreach (KeyValuePair<uint, string> pair in module.Names)
            {
                if (pair.Value != name)
                {
                    continue;
                }

                if (!module.Variables.TryGetValue(pair.Key, out int variableIndex))
                {
                    continue;
                }

                bound = PatchVariable(module, context, variableIndex, name, bound);
            }
        }

        if (context.WordPatches.Count == 0)
        {
            return spirv;
        }

        // Patches target the original instruction arrays; insertions are only emitted
        // during the rebuild, so the patched indices stay valid.
        foreach (KeyValuePair<(int instruction, int word), uint> patch in context.WordPatches)
        {
            module.Instructions[patch.Key.instruction][patch.Key.word] = patch.Value;
        }

        module.Header[BoundWordIndex] = Math.Max(bound, module.Header[BoundWordIndex]);
        return Rebuild(module, context.InsertAfter);
    }

    private static uint PatchVariable(
        Module module,
        PatchContext context,
        int variableIndex,
        string name,
        uint bound)
    {
        uint[] variable = module.Instructions[variableIndex];
        uint variableId = variable[2];
        uint pointerTypeId = variable[1];

        if (!module.TypePointers.TryGetValue(pointerTypeId, out int pointerIndex))
        {
            throw new ShaderCompilationException(
                $"Cannot mark depth texture '{name}': its variable does not reference an OpTypePointer.");
        }

        uint[] pointer = module.Instructions[pointerIndex];
        uint imageTypeId = pointer[3];
        if (!module.TypeImages.TryGetValue(imageTypeId, out int imageIndex))
        {
            throw new ShaderCompilationException(
                $"Cannot mark depth texture '{name}': its variable is not a sampled image (OpTypeImage).");
        }

        uint[] image = module.Instructions[imageIndex];
        if (image[4] == 1)
        {
            // Already a depth image, nothing to do.
            return bound;
        }

        // Clone the image type with Depth = 1, or reuse the clone made for another
        // texture sharing this declaration.
        if (!context.ImageTypeMap.TryGetValue(imageTypeId, out uint newImageTypeId))
        {
            newImageTypeId = bound++;
            uint[] newImage = (uint[])image.Clone();
            newImage[1] = newImageTypeId;
            newImage[4] = 1;
            AddInsertion(context.InsertAfter, imageIndex, newImage);
            context.ImageTypeMap.Add(imageTypeId, newImageTypeId);
        }

        // Find (or clone) the pointer type referencing the depth image clone. Reusing an
        // existing pointer type with the same storage class and pointee is required:
        // a duplicate declaration would be invalid SPIR-V.
        (uint storageClass, uint pointee) pointerKey = (pointer[2], newImageTypeId);
        if (!context.PointerTypeMap.TryGetValue(pointerKey, out uint newPointerTypeId))
        {
            newPointerTypeId = FindPointerType(module, pointer[2], newImageTypeId);
            if (newPointerTypeId == 0)
            {
                newPointerTypeId = bound++;
                uint[] newPointer = (uint[])pointer.Clone();
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
        if (module.LoadsByPointer.TryGetValue(variableId, out List<int>? loadIndices))
        {
            foreach (int loadIndex in loadIndices)
            {
                uint[] load = module.Instructions[loadIndex];
                context.WordPatches[(loadIndex, 1)] = newImageTypeId;
                loadedImageIds.Add(load[2]);
            }
        }

        // Rewire every OpSampledImage wrapping those loads. The sampled image type may be
        // shared with other textures, so it is cloned (once per declaration) as well.
        HashSet<uint> sampledImageIds = new();
        foreach (uint loadedImageId in loadedImageIds)
        {
            if (!module.SampledImagesByImage.TryGetValue(loadedImageId, out List<int>? sampledIndices))
            {
                continue;
            }

            foreach (int sampledIndex in sampledIndices)
            {
                uint[] sampledImage = module.Instructions[sampledIndex];
                uint sampledImageTypeId = sampledImage[1];

                if (!context.SampledImageTypeMap.TryGetValue(sampledImageTypeId, out uint newSampledImageTypeId))
                {
                    if (!module.TypeSampledImages.TryGetValue(sampledImageTypeId, out int typeSampledImageIndex))
                    {
                        throw new ShaderCompilationException(
                            $"Cannot mark depth texture '{name}': OpSampledImage references an unknown type.");
                    }

                    newSampledImageTypeId = bound++;
                    uint[] newTypeSampledImage = (uint[])module.Instructions[typeSampledImageIndex].Clone();
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
            if (!module.ImagesBySampledImage.TryGetValue(sampledImageId, out List<int>? imageIndices))
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

    /// <summary>
    /// Finds an existing OpTypePointer with the given storage class and pointee type,
    /// returning its result id (0 when none exists). Clones inserted so far are included
    /// so repeated clones are never emitted.
    /// </summary>
    private static uint FindPointerType(Module module, uint storageClass, uint pointeeTypeId)
    {
        foreach (KeyValuePair<uint, int> pair in module.TypePointers)
        {
            uint[] pointer = module.Instructions[pair.Value];
            if (pointer[2] == storageClass && pointer[3] == pointeeTypeId)
            {
                return pair.Key;
            }
        }

        return 0;
    }

    private static void AddInsertion(Dictionary<int, List<uint[]>> insertAfter, int instructionIndex, uint[] words)
    {
        if (!insertAfter.TryGetValue(instructionIndex, out List<uint[]>? list))
        {
            list = new List<uint[]>();
            insertAfter.Add(instructionIndex, list);
        }

        list.Add(words);
    }

    private static Module Parse(byte[] spirv)
    {
        if (spirv.Length < HeaderWordCount * 4 || spirv.Length % 4 != 0)
        {
            throw new ShaderCompilationException("Malformed SPIR-V module: invalid byte length.");
        }

        uint[] words = new uint[spirv.Length / 4];
        Buffer.BlockCopy(spirv, 0, words, 0, spirv.Length);

        if (words[0] != MagicNumber)
        {
            throw new ShaderCompilationException("Malformed SPIR-V module: bad magic number.");
        }

        List<uint[]> instructions = new();
        int offset = HeaderWordCount;
        while (offset < words.Length)
        {
            uint first = words[offset];
            int wordCount = (int)(first >> 16);
            if (wordCount == 0 || offset + wordCount > words.Length)
            {
                throw new ShaderCompilationException("Malformed SPIR-V module: truncated instruction stream.");
            }

            uint[] instruction = new uint[wordCount];
            Array.Copy(words, offset, instruction, 0, wordCount);
            instructions.Add(instruction);
            offset += wordCount;
        }

        uint[] header = new uint[HeaderWordCount];
        Array.Copy(words, header, HeaderWordCount);

        Module module = new() { Header = header, Instructions = instructions };

        for (int i = 0; i < instructions.Count; i++)
        {
            uint[] instruction = instructions[i];
            ushort opcode = (ushort)(instruction[0] & 0xFFFF);
            switch (opcode)
            {
                case OpName:
                    module.Names[instruction[1]] = ReadString(instruction, 2);
                    break;
                case OpVariable:
                    module.Variables[instruction[2]] = i;
                    break;
                case OpTypeImage:
                    module.TypeImages[instruction[1]] = i;
                    break;
                case OpTypePointer:
                    module.TypePointers[instruction[1]] = i;
                    break;
                case OpTypeSampledImage:
                    module.TypeSampledImages[instruction[1]] = i;
                    break;
                case OpLoad:
                    AddUsage(module.LoadsByPointer, instruction[3], i);
                    break;
                case OpSampledImage:
                    AddUsage(module.SampledImagesByImage, instruction[3], i);
                    break;
                case OpImage:
                    AddUsage(module.ImagesBySampledImage, instruction[3], i);
                    break;
            }
        }

        return module;
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

    private static string ReadString(uint[] instruction, int wordOffset)
    {
        int byteCount = (instruction.Length - wordOffset) * 4;
        byte[] bytes = new byte[byteCount];
        Buffer.BlockCopy(instruction, wordOffset * 4, bytes, 0, byteCount);

        int length = Array.IndexOf<byte>(bytes, 0);
        if (length < 0)
        {
            length = bytes.Length;
        }

        return Encoding.UTF8.GetString(bytes, 0, length);
    }

    private static byte[] Rebuild(
        Module module,
        Dictionary<int, List<uint[]>> insertAfter)
    {
        int wordCount = HeaderWordCount;
        for (int i = 0; i < module.Instructions.Count; i++)
        {
            wordCount += module.Instructions[i].Length;
            if (insertAfter.TryGetValue(i, out List<uint[]>? inserted))
            {
                for (int j = 0; j < inserted.Count; j++)
                {
                    wordCount += inserted[j].Length;
                }
            }
        }

        uint[] words = new uint[wordCount];
        Array.Copy(module.Header, words, HeaderWordCount);

        int offset = HeaderWordCount;
        for (int i = 0; i < module.Instructions.Count; i++)
        {
            uint[] instruction = module.Instructions[i];
            instruction.CopyTo(words, offset);
            offset += instruction.Length;

            if (insertAfter.TryGetValue(i, out List<uint[]>? inserted))
            {
                for (int j = 0; j < inserted.Count; j++)
                {
                    inserted[j].CopyTo(words, offset);
                    offset += inserted[j].Length;
                }
            }
        }

        byte[] result = new byte[words.Length * 4];
        Buffer.BlockCopy(words, 0, result, 0, result.Length);
        return result;
    }
}
