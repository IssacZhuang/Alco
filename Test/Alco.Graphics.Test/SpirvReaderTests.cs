using Alco.Graphics;
using Alco.Graphics.Spirv;
using NUnit.Framework;

namespace Alco.Graphics.Test;

/// <summary>
/// Unit tests for <see cref="SpirvReader"/> and <see cref="SpirvModule"/>.
/// Tests binary parsing, instruction indexing, decoration lookup, and round-trip serialization.
/// </summary>
[TestFixture]
public class SpirvReaderTests
{
    private const uint Magic = 0x07230203;
    private const uint Version = 0x00010300; // SPIR-V 1.3
    private const uint Generator = 0;

    /// <summary>
    /// Builds a minimal SPIR-V module from raw instruction words.
    /// </summary>
    private static byte[] BuildModule(uint bound, params uint[][] instructions)
    {
        int totalWords = 5; // header
        foreach (uint[] inst in instructions)
        {
            totalWords += inst.Length;
        }

        uint[] words = new uint[totalWords];
        words[0] = Magic;
        words[1] = Version;
        words[2] = Generator;
        words[3] = bound;
        words[4] = 0; // schema

        int offset = 5;
        foreach (uint[] inst in instructions)
        {
            Array.Copy(inst, 0, words, offset, inst.Length);
            offset += inst.Length;
        }

        byte[] result = new byte[words.Length * 4];
        Buffer.BlockCopy(words, 0, result, 0, result.Length);
        return result;
    }

    /// <summary>Creates an instruction word array with the proper high-16 word count.</summary>
    private static uint[] Inst(ushort opCode, params uint[] operands)
    {
        uint[] words = new uint[operands.Length + 1];
        words[0] = ((uint)(operands.Length + 1) << 16) | opCode;
        Array.Copy(operands, 0, words, 1, operands.Length);
        return words;
    }

    [Test(Description = "A valid module parses without error")]
    public void Parse_ValidModule_ReturnsInstructions()
    {
        byte[] spirv = BuildModule(
            bound: 10,
            Inst((ushort)SpirvOp.MemoryModel, 0, 1), // Logical, GLSL450
            Inst((ushort)SpirvOp.EntryPoint, 4, 0, 5, 0) // Fragment %5 "main"
        );

        SpirvModule module = SpirvReader.Parse(spirv);

        Assert.That(module.Header[0], Is.EqualTo(Magic));
        Assert.That(module.Bound, Is.EqualTo(10));
        Assert.That(module.Instructions.Count, Is.EqualTo(2));
    }

    [Test(Description = "A bad magic number throws")]
    public void Parse_BadMagic_Throws()
    {
        byte[] bad = new byte[20];
        Assert.Throws<ShaderReflectionException>(() => SpirvReader.Parse(bad));
    }

    [Test(Description = "Truncated instruction stream throws")]
    public void Parse_TruncatedStream_Throws()
    {
        // Header only says 5 words, but add a partial instruction.
        uint[] words = { Magic, Version, Generator, 2, 0, 0x00020000 }; // says 2 words but only 1 present
        byte[] spirv = new byte[words.Length * 4];
        Buffer.BlockCopy(words, 0, spirv, 0, spirv.Length);

        Assert.Throws<ShaderReflectionException>(() => SpirvReader.Parse(spirv));
    }

    [Test(Description = "Names index maps ID to name string")]
    public void Parse_OpName_IndexedCorrectly()
    {
        // OpName %1 "foo" → 3 words: header, target(1), string
        // "foo" in UTF-8 = 0x6F6F66 (little-endian) padded to 4 bytes
        byte[] spirv = BuildModule(
            bound: 5,
            Inst((ushort)SpirvOp.Name, 1, 0x6F6F66), // %1 = "foo"
            Inst((ushort)SpirvOp.MemoryModel, 0, 1)
        );

        SpirvModule module = SpirvReader.Parse(spirv);

        Assert.That(module.GetName(1), Is.EqualTo("foo"));
    }

    [Test(Description = "Decorations are indexed by target ID")]
    public void Parse_OpDecorate_IndexedCorrectly()
    {
        byte[] spirv = BuildModule(
            bound: 5,
            // OpDecorate %1 Binding 3
            Inst((ushort)SpirvOp.Decorate, 1, (uint)SpirvDecoration.Binding, 3),
            // OpDecorate %1 DescriptorSet 0
            Inst((ushort)SpirvOp.Decorate, 1, (uint)SpirvDecoration.DescriptorSet, 0),
            Inst((ushort)SpirvOp.MemoryModel, 0, 1)
        );

        SpirvModule module = SpirvReader.Parse(spirv);

        Assert.That(module.HasDecoration(1, SpirvDecoration.Binding), Is.True);
        Assert.That(module.GetDecorationValue(1, SpirvDecoration.Binding), Is.EqualTo(3u));
        Assert.That(module.GetDecorationValue(1, SpirvDecoration.DescriptorSet), Is.EqualTo(0u));
        Assert.That(module.HasDecoration(1, SpirvDecoration.Location), Is.False);
    }

    [Test(Description = "Member decorations are indexed by (struct, member)")]
    public void Parse_OpMemberDecorate_IndexedCorrectly()
    {
        byte[] spirv = BuildModule(
            bound: 5,
            // OpMemberDecorate %1, member 0, Offset 16
            Inst((ushort)SpirvOp.MemberDecorate, 1, 0, (uint)SpirvDecoration.Offset, 16),
            Inst((ushort)SpirvOp.MemoryModel, 0, 1)
        );

        SpirvModule module = SpirvReader.Parse(spirv);

        Assert.That(module.HasMemberDecoration(1, 0, SpirvDecoration.Offset), Is.True);
        Assert.That(module.GetMemberDecorationValue(1, 0, SpirvDecoration.Offset), Is.EqualTo(16u));
    }

    [Test(Description = "Result ID index maps ID to instruction")]
    public void Parse_TypeAndValue_IndexedByResultId()
    {
        byte[] spirv = BuildModule(
            bound: 10,
            Inst((ushort)SpirvOp.TypeFloat, 1, 32),     // %1 = float32
            Inst((ushort)SpirvOp.TypeVector, 2, 1, 4),   // %2 = vec4 of %1
            Inst((ushort)SpirvOp.TypePointer, 3, 1, 2),   // %3 = ptr(UniformConstant, %2)
            Inst((ushort)SpirvOp.Variable, 3, 4, 0),      // %4 = var ptr(3) storage=0
            Inst((ushort)SpirvOp.MemoryModel, 0, 1)
        );

        SpirvModule module = SpirvReader.Parse(spirv);

        Assert.That(module.GetInstruction(1), Is.Not.Null);
        Assert.That((SpirvOp)module.GetInstruction(1)!.OpCode, Is.EqualTo(SpirvOp.TypeFloat));
        Assert.That(module.GetInstruction(2), Is.Not.Null);
        Assert.That((SpirvOp)module.GetInstruction(2)!.OpCode, Is.EqualTo(SpirvOp.TypeVector));
        Assert.That(module.GetInstruction(4), Is.Not.Null);
        Assert.That((SpirvOp)module.GetInstruction(4)!.OpCode, Is.EqualTo(SpirvOp.Variable));
        Assert.That(module.GetInstruction(99), Is.Null);
    }

    [Test(Description = "Round-trip serialization preserves instruction data")]
    public void ToBytes_RoundTrip_PreservesData()
    {
        byte[] original = BuildModule(
            bound: 10,
            Inst((ushort)SpirvOp.Name, 1, 0x6F6F66), // "foo"
            Inst((ushort)SpirvOp.MemoryModel, 0, 1)
        );

        SpirvModule module = SpirvReader.Parse(original);
        byte[] rebuilt = module.ToBytes();

        Assert.That(rebuilt, Is.EqualTo(original));
    }

    [Test(Description = "ToBytes with insertions emits extra instructions in order")]
    public void ToBytes_WithInsertions_AppendsAfterInstruction()
    {
        byte[] baseModule = BuildModule(
            bound: 10,
            Inst((ushort)SpirvOp.MemoryModel, 0, 1),
            Inst((ushort)SpirvOp.Name, 1, 0x6F6F66)
        );

        SpirvModule module = SpirvReader.Parse(baseModule);

        SpirvInstruction extra = SpirvInstruction.Create(
            (ushort)SpirvOp.Name, 2, 0x726162); // "bar"

        var insertAfter = new Dictionary<int, List<SpirvInstruction>>
        {
            { 0, new List<SpirvInstruction> { extra } }
        };

        byte[] result = module.ToBytes(insertAfter);

        // Re-parse and verify the extra instruction is there.
        SpirvModule reparsed = SpirvReader.Parse(result);
        Assert.That(reparsed.GetName(2), Is.EqualTo("bar"));
    }

    [Test(Description = "Instruction Clone produces an independent copy")]
    public void Instruction_Clone_IsIndependent()
    {
        SpirvInstruction inst = SpirvInstruction.Create(
            (ushort)SpirvOp.TypeFloat, 1, 32);
        SpirvInstruction clone = inst.Clone();

        clone[2] = 16; // Change width in clone

        Assert.That(inst[2], Is.EqualTo(32u));  // Original unchanged
        Assert.That(clone[2], Is.EqualTo(16u)); // Clone modified
    }
}
