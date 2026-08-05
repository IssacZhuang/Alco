using System.Text;

namespace Alco.Graphics.Spirv;

/// <summary>
/// Represents a single SPIR-V instruction. The underlying <see cref="Words"/> array is mutable
/// to support byte-level rewriting (used by <c>SpirvDepthTexturePatcher</c>).
/// </summary>
public sealed class SpirvInstruction
{
    internal readonly uint[] Words;

    public SpirvInstruction(uint[] words) => Words = words;

    /// <summary>The SPIR-V opcode (low 16 bits of word 0).</summary>
    public ushort OpCode => (ushort)(Words[0] & 0xFFFF);

    /// <summary>The total word count of this instruction (equal to <see cref="Words"/>.Length).</summary>
    public int WordCount => Words.Length;

    /// <summary>Gets or sets a word in the instruction by index.</summary>
    public uint this[int index]
    {
        get => Words[index];
        set => Words[index] = value;
    }

    /// <summary>Creates a copy with a new underlying word array.</summary>
    public SpirvInstruction Clone()
    {
        uint[] copy = GC.AllocateUninitializedArray<uint>(Words.Length);
        Array.Copy(Words, copy, Words.Length);
        return new SpirvInstruction(copy);
    }

    /// <summary>
    /// Reads a null-terminated UTF-8 string starting at the given word offset
    /// and extending to the end of the instruction.
    /// </summary>
    public string ReadString(int startWord)
    {
        int byteCount = (Words.Length - startWord) * 4;
        if (byteCount <= 0)
        {
            return string.Empty;
        }

        byte[] bytes = new byte[byteCount];
        Buffer.BlockCopy(Words, startWord * 4, bytes, 0, byteCount);

        int length = Array.IndexOf(bytes, (byte)0);
        if (length < 0)
        {
            length = byteCount;
        }

        return Encoding.UTF8.GetString(bytes, 0, length);
    }

    /// <summary>Creates a new instruction from an opcode and operand words.</summary>
    public static SpirvInstruction Create(ushort opCode, params uint[] operands)
    {
        uint[] words = new uint[operands.Length + 1];
        words[0] = ((uint)(operands.Length + 1) << 16) | opCode;
        Array.Copy(operands, 0, words, 1, operands.Length);
        return new SpirvInstruction(words);
    }
}
