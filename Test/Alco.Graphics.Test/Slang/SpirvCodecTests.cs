using Alco.Graphics;
using NUnit.Framework;

namespace Alco.Graphics.Test;

/// <summary>
/// Unit tests for the shared SPIR-V word-level utilities: opcode registry
/// spot values from the specification, little-endian word/byte conversion,
/// instruction-header decoding and module assembly round-trips.
/// </summary>
[TestFixture]
public class SpirvCodecTests
{
    [Test]
    public void OpcodesMatchSpecificationValues()
    {
        Assert.That((uint)SpirvOp.Nop, Is.EqualTo(0u));
        Assert.That((uint)SpirvOp.Name, Is.EqualTo(5u));
        Assert.That((uint)SpirvOp.TypeInt, Is.EqualTo(21u));
        Assert.That((uint)SpirvOp.Constant, Is.EqualTo(43u));
        Assert.That((uint)SpirvOp.Function, Is.EqualTo(54u));
        Assert.That((uint)SpirvOp.FunctionEnd, Is.EqualTo(56u));
        Assert.That((uint)SpirvOp.Variable, Is.EqualTo(59u));
        Assert.That((uint)SpirvOp.Phi, Is.EqualTo(245u));
        Assert.That((uint)SpirvOp.LoopMerge, Is.EqualTo(246u));
        Assert.That((uint)SpirvOp.SelectionMerge, Is.EqualTo(247u));
        Assert.That((uint)SpirvOp.Label, Is.EqualTo(248u));
        Assert.That((uint)SpirvOp.Branch, Is.EqualTo(249u));
        Assert.That((uint)SpirvOp.BranchConditional, Is.EqualTo(250u));
        Assert.That((uint)SpirvOp.Switch, Is.EqualTo(251u));
        Assert.That((uint)SpirvOp.Kill, Is.EqualTo(252u));
        Assert.That((uint)SpirvOp.Return, Is.EqualTo(253u));
        Assert.That((uint)SpirvOp.ReturnValue, Is.EqualTo(254u));
        Assert.That((uint)SpirvOp.Unreachable, Is.EqualTo(255u));
        Assert.That((uint)SpirvOp.TerminateRayKHR, Is.EqualTo(4449u));
        Assert.That((uint)SpirvOp.Max, Is.EqualTo(0x7fffffffu));
    }

    [Test]
    public void BytesToWordsDecodesLittleEndianAndDropsIncompleteTrailingWord()
    {
        // Magic word 0x07230203 little-endian, one full word, two stray bytes.
        byte[] bytes = [0x03, 0x02, 0x23, 0x07, 0xAB, 0xCD];
        uint[] words = SpirvCodec.BytesToWords(bytes);
        Assert.That(words, Is.EqualTo(new[] { 0x0723_0203u }));
    }

    [Test]
    public void WordsToBytesRoundTripsThroughBytesToWords()
    {
        uint[] words = [SpirvCodec.Magic, 0x0001_0300u, 7u, 100u, 0u, 0x0003_00F7u, 27u, 0u];
        Assert.That(SpirvCodec.BytesToWords(SpirvCodec.WordsToBytes(words)), Is.EqualTo(words));
    }

    [Test]
    public void TryDecodeInstructionReadsCountAndOpcode()
    {
        uint[] words = [SpirvCodec.Magic, 0, 0, 100, 0, (3u << 16) | (uint)SpirvOp.SelectionMerge, 27u, 0u];
        Assert.That(SpirvCodec.TryDecodeInstruction(words, SpirvCodec.HeaderWordCount, out SpirvInstruction instruction), Is.True);
        Assert.That(instruction.Offset, Is.EqualTo(5));
        Assert.That(instruction.WordCount, Is.EqualTo(3));
        Assert.That(instruction.Op, Is.EqualTo(SpirvOp.SelectionMerge));
    }

    [Test]
    public void TryDecodeInstructionRejectsMalformedFraming()
    {
        uint[] words = [SpirvCodec.Magic, 0, 0, 100, 0, (3u << 16) | (uint)SpirvOp.Switch];
        Assert.That(SpirvCodec.TryDecodeInstruction(words, -1, out _), Is.False);
        Assert.That(SpirvCodec.TryDecodeInstruction(words, words.Length, out _), Is.False);
        // Word count zero can only be forged via a raw zero opcode word.
        uint[] zeroCount = [SpirvCodec.Magic, 0, 0, 100, 0, 0u];
        Assert.That(SpirvCodec.TryDecodeInstruction(zeroCount, 5, out _), Is.False);
        // Declared word count overruns the module.
        Assert.That(SpirvCodec.TryDecodeInstruction(words, 5, out _), Is.False);
    }

    [Test]
    public void DecodeInstructionThrowsOnMalformedFraming()
    {
        uint[] words = [SpirvCodec.Magic, 0, 0, 100, 0, (2u << 16) | (uint)SpirvOp.Branch, 9u];
        Assert.That(() => SpirvCodec.DecodeInstruction(words, 9), Throws.InvalidOperationException);
    }

    [Test]
    public void AssemblerEmitsHeaderBoundAndBody()
    {
        SpirvAssembler assembler = new(version: 0x0001_0500, generator: 42u);
        uint resultType = assembler.AllocateId();
        uint constant = assembler.AllocateId();
        assembler.Emit(SpirvOp.TypeInt, resultType, 32u, 1u);
        assembler.Emit(SpirvOp.Constant, resultType, constant, 0u);
        assembler.EnsureBound(77u);

        uint[] words = assembler.ToWords();
        Assert.That(words.Length, Is.EqualTo(SpirvCodec.HeaderWordCount + 8));
        Assert.That(words[0], Is.EqualTo(SpirvCodec.Magic));
        Assert.That(words[1], Is.EqualTo(0x0001_0500u));
        Assert.That(words[2], Is.EqualTo(42u));
        Assert.That(words[3], Is.EqualTo(77u));
        Assert.That(words[4], Is.EqualTo(0u));
        Assert.That(words[5], Is.EqualTo((4u << 16) | (uint)SpirvOp.TypeInt));
        Assert.That(words[9], Is.EqualTo((4u << 16) | (uint)SpirvOp.Constant));
    }

    [Test]
    public void AssemblerSplicesRawWordsAndRoundTrips()
    {
        SpirvAssembler assembler = new();
        uint[] branchInstruction = [(2u << 16) | (uint)SpirvOp.Branch, 9u];
        assembler.EmitWords(branchInstruction);
        assembler.Emit(SpirvOp.Return);

        uint[] words = assembler.ToWords();
        Assert.That(words[5..], Is.EqualTo(new uint[] { branchInstruction[0], branchInstruction[1], (1u << 16) | (uint)SpirvOp.Return }));
        Assert.That(SpirvCodec.TryDecodeInstruction(words, 5, out SpirvInstruction branch), Is.True);
        Assert.That(branch.Op, Is.EqualTo(SpirvOp.Branch));
        Assert.That(SpirvCodec.TryDecodeInstruction(words, 7, out SpirvInstruction ret), Is.True);
        Assert.That(ret.Op, Is.EqualTo(SpirvOp.Return));
        Assert.That(SpirvCodec.BytesToWords(assembler.ToBytes()), Is.EqualTo(words));
    }
}
