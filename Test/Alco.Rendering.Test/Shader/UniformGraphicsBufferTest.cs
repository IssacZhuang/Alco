using System.Numerics;
using Alco.Graphics;
using Alco.ShaderCompiler;
#nullable enable

using NUnit.Framework;

namespace Alco.Rendering.Test;

// ─────────────────────────────────────────────────────────────────────────────
// UniformGraphicsBuffer tests: a reflection-driven uniform buffer built from a
// ShaderUniformBlock. Asserts the name-keyed write contract (offsets, sizes,
// array spans, validation) through the staging bytes — the NoGPU device makes
// the GPU upload itself a no-op, so the CPU-side layout discipline is the
// testable surface. Uses the NoGPU device; GetBufferContent reads the staging
// back through the exposed test seam.
// ─────────────────────────────────────────────────────────────────────────────
[TestFixture]
public class UniformGraphicsBufferTest
{
    private const string Module = """
        #language slang 2025
        module test_uniform_buffer;

        cbuffer TestData : register(b0, space2)
        {
            float pulseSpeed;
            float3 pulseColor;
            int levelIndex;
            uint flags;
            bool enabled;
            float4 weights[3];
        }
        """;

    [Test]
    public void SetValue_WritesAtReflectedOffsets_TypedMembers()
    {
        ShaderUniformBlock block = ReflectBlock();

        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem();
        using UniformGraphicsBuffer buffer = host.RenderingSystem
            .CreateUniformGraphicsBuffer(block, "test_uniform");

        buffer.SetValue("pulseSpeed", 2.5f);
        buffer.SetValue("pulseColor", new Vector3(1f, 0.5f, 0.25f));
        buffer.SetValue("levelIndex", 7);
        buffer.SetValue("flags", 0x1234u);
        buffer.SetValue("enabled", true);
        buffer.SetValues("weights", new Vector4[]
        {
            new(1, 2, 3, 4), new(5, 6, 7, 8), new(9, 10, 11, 12),
        });

        ShaderUniformMember level = FindMember(block, "levelIndex");
        Assert.That(BitConverter.SingleToInt32Bits(ReadFloat(buffer, level.OffsetBytes)), Is.EqualTo(7),
            "int blits as its 32-bit image");
        ShaderUniformMember enabled = FindMember(block, "enabled");
        Assert.That(BitConverter.SingleToInt32Bits(ReadFloat(buffer, enabled.OffsetBytes)), Is.EqualTo(1),
            "bool marshals to 1");
        ShaderUniformMember weights = FindMember(block, "weights");
        Assert.That(ReadFloat(buffer, weights.OffsetBytes + 4 * sizeof(float) * 2), Is.EqualTo(9f),
            "array elements land contiguously at the reflected offset");
    }

    [Test]
    public void SetValue_UnknownMember_ThrowsListingValidNames()
    {
        ShaderUniformBlock block = ReflectBlock();

        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem();
        using UniformGraphicsBuffer buffer = host.RenderingSystem
            .CreateUniformGraphicsBuffer(block, "test_uniform");

        Assert.That(() => buffer.SetValue("typo", 1f),
            Throws.TypeOf<KeyNotFoundException>().With.Message.Contains("pulseSpeed"));
    }

    [Test]
    public void SetValue_OversizedValue_Throws()
    {
        ShaderUniformBlock block = ReflectBlock();

        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem();
        using UniformGraphicsBuffer buffer = host.RenderingSystem
            .CreateUniformGraphicsBuffer(block, "test_uniform");

        Assert.That(() => buffer.SetValue("pulseSpeed", new Vector4(1, 2, 3, 4)),
            Throws.TypeOf<ArgumentException>(), "a float member cannot take a float4 value");
    }

    [Test]
    public void SetValues_WrongElementCount_Throws()
    {
        ShaderUniformBlock block = ReflectBlock();

        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem();
        using UniformGraphicsBuffer buffer = host.RenderingSystem
            .CreateUniformGraphicsBuffer(block, "test_uniform");

        Assert.That(() => buffer.SetValues("weights", new[] { new Vector4(1, 2, 3, 4) }),
            Throws.TypeOf<ArgumentException>().With.Message.Contains("3"));
        Assert.That(() => buffer.SetValues("pulseSpeed", new[] { 1f }),
            Throws.TypeOf<ArgumentException>(), "a plain member is not an array");
    }

    [Test]
    public void Reflection_RecordsTypedMembersAndArrays()
    {
        ShaderUniformBlock block = ReflectBlock();

        Assert.Multiple(() =>
        {
            Assert.That(block.UnsupportedMemberReason, Is.Null);
            ShaderUniformMember level = FindMember(block, "levelIndex");
            Assert.That(level.ScalarType, Is.EqualTo(ShaderUniformScalarType.Int32));
            Assert.That(level.ComponentCount, Is.EqualTo(1));
            ShaderUniformMember color = FindMember(block, "pulseColor");
            Assert.That(color.ScalarType, Is.EqualTo(ShaderUniformScalarType.Float32));
            Assert.That(color.ComponentCount, Is.EqualTo(3));
            Assert.That(color.OffsetBytes, Is.EqualTo(16u), "float3 lands at the next 16-byte slot");
            ShaderUniformMember flags = FindMember(block, "flags");
            Assert.That(flags.ScalarType, Is.EqualTo(ShaderUniformScalarType.UInt32));
            ShaderUniformMember enabled = FindMember(block, "enabled");
            Assert.That(enabled.ScalarType, Is.EqualTo(ShaderUniformScalarType.Bool32));
            ShaderUniformMember weights = FindMember(block, "weights");
            Assert.That(weights.ElementCount, Is.EqualTo(3u));
            Assert.That(weights.ComponentCount, Is.EqualTo(4));
            Assert.That(weights.SizeBytes, Is.EqualTo(48u), "stride × count");
        });
    }

    private static ShaderUniformMember FindMember(ShaderUniformBlock block, string name)
        => block.Members.First(member => member.Name == name);

    private static ShaderUniformBlock ReflectBlock()
    {
        using SlangModuleSystem system = new(new SlangCompilerOptions
        {
            Resolver = path => path.Contains("test_uniform_buffer") ? Module : null,
        }, null);
        return system.GetModuleReflection("test_uniform_buffer")
            .UniformBlocks.First(block => block.Name == "TestData");
    }

    // The staging view of the buffer's bytes, for assertions. Test seam: the
    // GPU upload is a no-op on NoGPU, so reading back the staged floats is the
    // observable contract.
    private static unsafe float ReadFloat(UniformGraphicsBuffer buffer, uint offset)
        => buffer.ReadStagingFloat(offset);
}
