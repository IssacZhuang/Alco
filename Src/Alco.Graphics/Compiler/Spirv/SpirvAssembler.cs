namespace Alco.Graphics;

// ─────────────────────────────────────────────────────────────────────────────
// SPIR-V module reassembly: appends instruction words while allocating
// result ids, then prefixes the five-word header. The inverse of walking a
// module with SpirvCodec.TryDecodeInstruction.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Assembles a SPIR-V module word stream. Ids are never inferred from emitted
/// words — callers allocate through <see cref="AllocateId"/> or raise the bound
/// explicitly with <see cref="EnsureBound"/>, so splicing pre-built words with
/// <see cref="EmitWords"/> stays side-effect free.
/// </summary>
public sealed class SpirvAssembler
{
    private readonly List<uint> _body = [];

    /// <summary>
    /// Starts an empty module. The version defaults to SPIR-V 1.3 — the
    /// dialect the compile pipeline pins for slang emission — and generator 0
    /// (unregistered); schema is always 0.
    /// </summary>
    /// <param name="version">The version word (e.g. <c>0x0001_0300</c> for SPIR-V 1.3).</param>
    /// <param name="generator">The generator word (0 = unregistered).</param>
    public SpirvAssembler(uint version = 0x0001_0300, uint generator = 0)
    {
        Version = version;
        Generator = generator;
    }

    /// <summary>The version header word.</summary>
    public uint Version { get; }

    /// <summary>The generator header word.</summary>
    public uint Generator { get; }

    /// <summary>The result-id bound: the highest id allocated so far.</summary>
    public uint Bound { get; private set; }

    /// <summary>Total words the assembled module will occupy (header included).</summary>
    public int WordCount => SpirvCodec.HeaderWordCount + _body.Count;

    /// <summary>Allocates the next result id, raising the bound.</summary>
    /// <returns>The fresh id.</returns>
    public uint AllocateId() => ++Bound;

    /// <summary>Raises the bound to include an externally chosen id.</summary>
    /// <param name="id">An id the caller emitted by other means.</param>
    public void EnsureBound(uint id)
    {
        if (id > Bound)
        {
            Bound = id;
        }
    }

    /// <summary>
    /// Emits an instruction from its opcode and operand words; the opcode
    /// word's count is derived from the operand span.
    /// </summary>
    /// <param name="op">The instruction opcode.</param>
    /// <param name="operands">Operand words after the opcode word.</param>
    public void Emit(SpirvOp op, ReadOnlySpan<uint> operands)
    {
        _body.Add(((uint)(operands.Length + 1) << 16) | (uint)op);
        for (int i = 0; i < operands.Length; i++)
        {
            _body.Add(operands[i]);
        }
    }

    /// <summary>
    /// Emits an instruction from its opcode and operand words; the opcode
    /// word's count is derived from the operand array.
    /// </summary>
    /// <param name="op">The instruction opcode.</param>
    /// <param name="operands">Operand words after the opcode word.</param>
    public void Emit(SpirvOp op, params uint[] operands) => Emit(op, (ReadOnlySpan<uint>)operands);

    /// <summary>
    /// Splices raw instruction words verbatim — the first word already carries
    /// its own count. The bound is not re-derived from the spliced ids.
    /// </summary>
    /// <param name="words">Complete instruction words, opcode word first.</param>
    public void EmitWords(ReadOnlySpan<uint> words)
    {
        for (int i = 0; i < words.Length; i++)
        {
            _body.Add(words[i]);
        }
    }

    /// <summary>Assembles the module: five header words (magic, version, generator, bound, schema) then the body.</summary>
    /// <returns>The complete module words.</returns>
    public uint[] ToWords()
    {
        uint[] words = new uint[SpirvCodec.HeaderWordCount + _body.Count];
        words[0] = SpirvCodec.Magic;
        words[1] = Version;
        words[2] = Generator;
        words[3] = Bound;
        for (int i = 0; i < _body.Count; i++)
        {
            words[SpirvCodec.HeaderWordCount + i] = _body[i];
        }
        return words;
    }

    /// <summary>Assembles the module as little-endian bytes.</summary>
    /// <returns>The complete module bytes.</returns>
    public byte[] ToBytes() => SpirvCodec.WordsToBytes(ToWords());
}
