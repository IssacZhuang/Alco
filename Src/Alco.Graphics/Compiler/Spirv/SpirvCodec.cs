namespace Alco.Graphics;

// ─────────────────────────────────────────────────────────────────────────────
// Word-level SPIR-V framing shared by compiler passes: header constants,
// little-endian word/byte conversion and instruction-header decoding. Pure
// data plumbing with no producer or backend knowledge — passes (such as
// SpirvNormalizer) and future patchers build on these primitives.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Decodes SPIR-V framing: little-endian byte/word conversion and instruction
/// headers (<c>(wordCount &lt;&lt; 16) | opcode</c>). Instructions are exposed
/// as <see cref="SpirvInstruction"/> locations over the word array — operand
/// words stay in place, so passes rewrite by splicing ranges instead of
/// materializing a module object model.
/// </summary>
public static class SpirvCodec
{
    /// <summary>The SPIR-V module magic number.</summary>
    public const uint Magic = 0x0723_0203;

    /// <summary>Fixed module header size in words: magic, version, generator, bound, schema.</summary>
    public const int HeaderWordCount = 5;

    /// <summary>Header word index of the result-id bound.</summary>
    public const int BoundWordIndex = 3;

    /// <summary>
    /// Decodes little-endian bytes to SPIR-V words, dropping an incomplete
    /// trailing word.
    /// </summary>
    /// <param name="bytes">Module bytes (four bytes per word); any trailing partial word is dropped.</param>
    public static uint[] BytesToWords(ReadOnlySpan<byte> bytes)
    {
        uint[] words = new uint[bytes.Length / 4];
        for (int i = 0; i < words.Length; i++)
        {
            int b = i * 4;
            words[i] = (uint)(bytes[b] | (bytes[b + 1] << 8) | (bytes[b + 2] << 16) | (bytes[b + 3] << 24));
        }
        return words;
    }

    /// <summary>Encodes SPIR-V words to little-endian bytes.</summary>
    /// <param name="words">Module words, starting with the five header words.</param>
    public static byte[] WordsToBytes(ReadOnlySpan<uint> words)
    {
        byte[] bytes = new byte[words.Length * 4];
        for (int i = 0; i < words.Length; i++)
        {
            uint word = words[i];
            int b = i * 4;
            bytes[b] = (byte)word;
            bytes[b + 1] = (byte)(word >> 8);
            bytes[b + 2] = (byte)(word >> 16);
            bytes[b + 3] = (byte)(word >> 24);
        }
        return bytes;
    }

    /// <summary>
    /// Decodes one instruction header at the given word offset.
    /// </summary>
    /// <param name="words">Module words, starting with the five header words.</param>
    /// <param name="offset">Word offset of the instruction's opcode word.</param>
    /// <param name="instruction">The decoded location and shape, or default on failure.</param>
    /// <returns>
    /// False when the offset is out of range or the word count is zero or
    /// overruns the module — callers decide their own error vocabulary.
    /// </returns>
    public static bool TryDecodeInstruction(ReadOnlySpan<uint> words, int offset, out SpirvInstruction instruction)
    {
        instruction = default;
        if (offset < 0 || offset >= words.Length)
        {
            return false;
        }
        uint word = words[offset];
        int wordCount = (int)(word >> 16);
        if (wordCount == 0 || wordCount > words.Length - offset)
        {
            return false;
        }
        instruction = new SpirvInstruction(offset, wordCount, (SpirvOp)(word & 0xFFFF));
        return true;
    }

    /// <summary>
    /// Decodes one instruction header, throwing on malformed framing.
    /// </summary>
    /// <param name="words">Module words, starting with the five header words.</param>
    /// <param name="offset">Word offset of the instruction's opcode word.</param>
    /// <exception cref="InvalidOperationException">
    /// The module ends mid-instruction or the word count is invalid.
    /// </exception>
    public static SpirvInstruction DecodeInstruction(ReadOnlySpan<uint> words, int offset)
        => TryDecodeInstruction(words, offset, out SpirvInstruction instruction)
            ? instruction
            : throw new InvalidOperationException($"invalid SPIR-V instruction at word {offset}");
}

/// <summary>
/// One decoded SPIR-V instruction: its location and shape over the module's
/// word array. Operand words live at <c>[Offset + 1, Offset + WordCount)</c>;
/// result ids (when the opcode has one) are at <c>Offset + 1</c> or
/// <c>Offset + 2</c> per the opcode's grammar — this type does not interpret them.
/// </summary>
/// <param name="offset">Word offset of the instruction's opcode word.</param>
/// <param name="wordCount">Total words including the opcode word.</param>
/// <param name="op">The instruction opcode.</param>
public readonly struct SpirvInstruction(int offset, int wordCount, SpirvOp op)
{
    /// <summary>Word offset of the instruction's opcode word.</summary>
    public int Offset { get; } = offset;

    /// <summary>Total words including the opcode word.</summary>
    public int WordCount { get; } = wordCount;

    /// <summary>The instruction opcode.</summary>
    public SpirvOp Op { get; } = op;
}
