using System.Text;

namespace Alco.Graphics.Spirv;

/// <summary>
/// A parsed SPIR-V module with instruction indexes for efficient lookup.
/// The instruction list and word arrays are mutable to support rewriting (see SpirvDepthTexturePatcher).
/// </summary>
public sealed class SpirvModule
{
    internal const uint MagicNumber = 0x07230203;
    internal const int HeaderWordCount = 5;
    internal const int BoundWordIndex = 3;

    /// <summary>Raw header words: [Magic, Version, Generator, Bound, Schema].</summary>
    public uint[] Header { get; }

    /// <summary>The Bound value from the header (settable for patcher that allocates new IDs).</summary>
    public uint Bound
    {
        get => Header[BoundWordIndex];
        set => Header[BoundWordIndex] = value;
    }

    /// <summary>All instructions in source order.</summary>
    public List<SpirvInstruction> Instructions { get; }

    /// <summary>Maps Result ID to the instruction that defines it.</summary>
    public Dictionary<uint, SpirvInstruction> ByResultId { get; } = new();

    /// <summary>Maps target ID to its <c>OpName</c> string (if present).</summary>
    public Dictionary<uint, string> Names { get; } = new();

    /// <summary>Maps target ID to all its decorations from <c>OpDecorate</c>.</summary>
    public Dictionary<uint, List<SpirvDecorationEntry>> Decorations { get; } = new();

    /// <summary>Maps (struct type ID, member index) to decorations from <c>OpMemberDecorate</c>.</summary>
    public Dictionary<(uint StructId, uint MemberIndex), List<SpirvDecorationEntry>> MemberDecorations { get; } = new();

    /// <summary>List of <c>OpEntryPoint</c> instructions.</summary>
    public List<SpirvInstruction> EntryPoints { get; } = new();

    /// <summary>List of <c>OpExecutionMode</c> instructions.</summary>
    public List<SpirvInstruction> ExecutionModes { get; } = new();

    internal SpirvModule(uint[] header, List<SpirvInstruction> instructions)
    {
        Header = header;
        Instructions = instructions;
    }

    internal void BuildIndexes()
    {
        for (int i = 0; i < Instructions.Count; i++)
        {
            SpirvInstruction inst = Instructions[i];

            switch ((SpirvOp)inst.OpCode)
            {
                case SpirvOp.Name:
                    Names[inst[1]] = inst.ReadString(2);
                    break;

                case SpirvOp.Decorate:
                    AddDecoration(Decorations, inst[1], inst[2], inst, 3);
                    break;

                case SpirvOp.MemberDecorate:
                    AddMemberDecoration(inst[1], inst[2], inst[3], inst, 4);
                    break;

                case SpirvOp.EntryPoint:
                    EntryPoints.Add(inst);
                    break;

                case SpirvOp.ExecutionMode:
                    ExecutionModes.Add(inst);
                    break;

                // Type declarations: Result ID at word[1]
                case SpirvOp.TypeVoid:
                case SpirvOp.TypeBool:
                case SpirvOp.TypeInt:
                case SpirvOp.TypeFloat:
                case SpirvOp.TypeVector:
                case SpirvOp.TypeMatrix:
                case SpirvOp.TypeImage:
                case SpirvOp.TypeSampler:
                case SpirvOp.TypeSampledImage:
                case SpirvOp.TypeArray:
                case SpirvOp.TypeRuntimeArray:
                case SpirvOp.TypeStruct:
                case SpirvOp.TypePointer:
                case SpirvOp.TypeFunction:
                    ByResultId[inst[1]] = inst;
                    break;

                // Value instructions: Type ID at word[1], Result ID at word[2]
                case SpirvOp.Variable:
                case SpirvOp.Load:
                case SpirvOp.Constant:
                case SpirvOp.Function:
                case SpirvOp.SampledImage:
                case SpirvOp.Image:
                case SpirvOp.AccessChain:
                    ByResultId[inst[2]] = inst;
                    break;
            }
        }
    }

    private void AddDecoration(
        Dictionary<uint, List<SpirvDecorationEntry>> dict,
        uint targetId, uint decoration, SpirvInstruction inst, int extraStart)
    {
        if (!dict.TryGetValue(targetId, out List<SpirvDecorationEntry>? list))
        {
            list = new List<SpirvDecorationEntry>();
            dict[targetId] = list;
        }

        uint[] extra = new uint[inst.WordCount - extraStart];
        for (int j = 0; j < extra.Length; j++)
        {
            extra[j] = inst[extraStart + j];
        }

        list.Add(new SpirvDecorationEntry((SpirvDecoration)decoration, extra));
    }

    private void AddMemberDecoration(
        uint structId, uint memberIndex, uint decoration, SpirvInstruction inst, int extraStart)
    {
        var key = (structId, memberIndex);
        if (!MemberDecorations.TryGetValue(key, out List<SpirvDecorationEntry>? list))
        {
            list = new List<SpirvDecorationEntry>();
            MemberDecorations[key] = list;
        }

        uint[] extra = new uint[inst.WordCount - extraStart];
        for (int j = 0; j < extra.Length; j++)
        {
            extra[j] = inst[extraStart + j];
        }

        list.Add(new SpirvDecorationEntry((SpirvDecoration)decoration, extra));
    }

    /// <summary>Tries to find the instruction that defines the given Result ID.</summary>
    public SpirvInstruction? GetInstruction(uint resultId)
        => ByResultId.TryGetValue(resultId, out SpirvInstruction? inst) ? inst : null;

    /// <summary>Gets the <c>OpName</c> string for the given ID, or null.</summary>
    public string? GetName(uint id)
        => Names.TryGetValue(id, out string? name) ? name : null;

    /// <summary>Checks whether the given ID has the specified decoration.</summary>
    public bool HasDecoration(uint targetId, SpirvDecoration decoration)
    {
        if (Decorations.TryGetValue(targetId, out List<SpirvDecorationEntry>? list))
        {
            foreach (SpirvDecorationEntry entry in list)
            {
                if (entry.Decoration == decoration)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Gets the first extra operand of a decoration, or 0 if not found.</summary>
    public uint GetDecorationValue(uint targetId, SpirvDecoration decoration)
    {
        if (Decorations.TryGetValue(targetId, out List<SpirvDecorationEntry>? list))
        {
            foreach (SpirvDecorationEntry entry in list)
            {
                if (entry.Decoration == decoration && entry.ExtraOperands.Length > 0)
                {
                    return entry.ExtraOperands[0];
                }
            }
        }

        return 0;
    }

    /// <summary>Gets the first extra operand of a member decoration, or 0 if not found.</summary>
    public uint GetMemberDecorationValue(uint structId, uint memberIndex, SpirvDecoration decoration)
    {
        if (MemberDecorations.TryGetValue((structId, memberIndex), out List<SpirvDecorationEntry>? list))
        {
            foreach (SpirvDecorationEntry entry in list)
            {
                if (entry.Decoration == decoration && entry.ExtraOperands.Length > 0)
                {
                    return entry.ExtraOperands[0];
                }
            }
        }

        return 0;
    }

    /// <summary>Checks whether a struct member has the specified decoration.</summary>
    public bool HasMemberDecoration(uint structId, uint memberIndex, SpirvDecoration decoration)
    {
        if (MemberDecorations.TryGetValue((structId, memberIndex), out List<SpirvDecorationEntry>? list))
        {
            foreach (SpirvDecorationEntry entry in list)
            {
                if (entry.Decoration == decoration)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Serializes this module (including any in-place word modifications) to a byte array,
    /// optionally inserting cloned instructions after specified positions.
    /// </summary>
    /// <param name="insertAfter">Maps instruction index to instructions to emit immediately after it.</param>
    public byte[] ToBytes(Dictionary<int, List<SpirvInstruction>>? insertAfter = null)
    {
        int totalWords = HeaderWordCount;
        for (int i = 0; i < Instructions.Count; i++)
        {
            totalWords += Instructions[i].WordCount;
            if (insertAfter?.TryGetValue(i, out List<SpirvInstruction>? inserted) == true)
            {
                for (int j = 0; j < inserted.Count; j++)
                {
                    totalWords += inserted[j].WordCount;
                }
            }
        }

        uint[] words = new uint[totalWords];
        Array.Copy(Header, words, HeaderWordCount);

        int offset = HeaderWordCount;
        for (int i = 0; i < Instructions.Count; i++)
        {
            SpirvInstruction inst = Instructions[i];
            Array.Copy(inst.Words, 0, words, offset, inst.WordCount);
            offset += inst.WordCount;

            if (insertAfter?.TryGetValue(i, out List<SpirvInstruction>? inserted) == true)
            {
                for (int j = 0; j < inserted.Count; j++)
                {
                    SpirvInstruction ins = inserted[j];
                    Array.Copy(ins.Words, 0, words, offset, ins.WordCount);
                    offset += ins.WordCount;
                }
            }
        }

        byte[] result = new byte[words.Length * 4];
        Buffer.BlockCopy(words, 0, result, 0, result.Length);
        return result;
    }
}

/// <summary>
/// A single SPIR-V decoration with its extra operands (e.g. the binding number for Binding).
/// </summary>
public readonly record struct SpirvDecorationEntry(SpirvDecoration Decoration, uint[] ExtraOperands);
