namespace Alco.Graphics.Spirv;

/// <summary>
/// Parses SPIR-V binary bytecode into a <see cref="SpirvModule"/>.
/// </summary>
public static class SpirvReader
{
    /// <summary>
    /// Parses SPIR-V bytecode into a module with full instruction indexes.
    /// </summary>
    /// <exception cref="ShaderReflectionException">Thrown if the bytecode is not valid SPIR-V.</exception>
    public static SpirvModule Parse(ReadOnlySpan<byte> spirv)
    {
        const int HeaderWordCount = SpirvModule.HeaderWordCount;

        if (spirv.Length < HeaderWordCount * 4 || spirv.Length % 4 != 0)
        {
            throw new ShaderReflectionException("Malformed SPIR-V module: invalid byte length.");
        }

        uint[] words = new uint[spirv.Length / 4];
        for (int i = 0; i < words.Length; i++)
        {
            words[i] = BitConverter.ToUInt32(spirv.Slice(i * 4, 4));
        }

        if (words[0] != SpirvModule.MagicNumber)
        {
            throw new ShaderReflectionException("Malformed SPIR-V module: bad magic number.");
        }

        List<SpirvInstruction> instructions = new();
        int offset = HeaderWordCount;
        while (offset < words.Length)
        {
            uint first = words[offset];
            int wordCount = (int)(first >> 16);
            if (wordCount == 0 || offset + wordCount > words.Length)
            {
                throw new ShaderReflectionException("Malformed SPIR-V module: truncated instruction stream.");
            }

            uint[] instWords = new uint[wordCount];
            Array.Copy(words, offset, instWords, 0, wordCount);
            instructions.Add(new SpirvInstruction(instWords));
            offset += wordCount;
        }

        uint[] header = new uint[HeaderWordCount];
        Array.Copy(words, header, HeaderWordCount);

        SpirvModule module = new(header, instructions);
        module.BuildIndexes();
        return module;
    }
}
