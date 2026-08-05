using Alco.Graphics;
using Alco.Rendering;
using NUnit.Framework;

namespace Alco.Rendering.Test;

/// <summary>
/// Tests for the dense resource mapping of <see cref="ShaderReflectionInfo"/>:
/// one resource id per settable shader variable (buffer or texture), with sampler
/// and counter companion entries excluded, exercised through real DXC compilation.
/// </summary>
[TestFixture]
public class TestShaderResourceMapping
{
    // One set per resource, packed with companions: a sampler paired to _albedo by
    // name, and the implicit counter DXC emits for a structured buffer used through
    // a function parameter ("counter.var._buffer", at a binding chosen by DXC).
    private const string PackedShader = @"
[[vk::binding(0, 0)]] cbuffer _data { float4 value; };
[[vk::binding(1, 0)]] Texture2D _albedo;
[[vk::binding(0+1, 1)]] SamplerState _albedoSampler;
[[vk::binding(2, 2)]] RWStructuredBuffer<float> _buffer;
[[vk::binding(3, 3)]] RWTexture2D<float4> _storage;

float Load(RWStructuredBuffer<float> buf) { return buf[0]; }

[shader(""compute"")]
[numthreads(1, 1, 1)]
void MainCS(uint3 id : SV_DispatchThreadID)
{
    _storage[id.xy] = _albedo.SampleLevel(_albedoSampler, float2(0, 0), 0) + value + Load(_buffer);
}
";

    [Test(Description = "Settable resources get dense ids in reflection order; sampler and counter companions are excluded")]
    public void Reflection_DenseResourceMapping_ExcludesCompanions()
    {
        ShaderModulesInfo modulesInfo = ShaderUtility.CompileHLSL(PackedShader, "packed_shader", default, 8);
        ShaderReflectionInfo info = modulesInfo.ReflectionInfo;

        Assert.That(info.BindGroups.Count, Is.EqualTo(4));
        Assert.That(info.ResourceCount, Is.EqualTo(4));

        // Sampler and counter companions are not settable resources.
        Assert.IsFalse(info.TryGetResourceId("_albedoSampler", out _));
        Assert.IsFalse(info.TryGetResourceId("counter.var._buffer", out _));

        // Id and name lookups agree with the resource locations.
        AssertResource(info, 0, "_data", 0, 0, BindingType.UniformBuffer);
        AssertResource(info, 1, "_albedo", 0, 1, BindingType.Texture);
        AssertResource(info, 2, "_buffer", 2, 2, BindingType.StorageBuffer);
        AssertResource(info, 3, "_storage", 3, 3, BindingType.StorageTexture);
    }

    private static void AssertResource(ShaderReflectionInfo info, uint id, string name, int group, uint binding, BindingType type)
    {
        Assert.IsTrue(info.TryGetResourceId(name, out uint actualId), name);
        Assert.That(actualId, Is.EqualTo(id), name);
        Assert.That(info.GetResourceName(id), Is.EqualTo(name));

        ShaderResourceLocation location = info.GetResourceLocation(id);
        Assert.That(location.Name, Is.EqualTo(name), name);
        Assert.That(location.GroupIndex, Is.EqualTo(group), name);
        Assert.That(location.Binding, Is.EqualTo(binding), name);
        Assert.That(location.Type, Is.EqualTo(type), name);

        Assert.IsTrue(info.TryGetResourceLocation(name, out ShaderResourceLocation byName), name);
        Assert.That(byName.GroupIndex, Is.EqualTo(location.GroupIndex), name);
        Assert.That(byName.EntryIndex, Is.EqualTo(location.EntryIndex), name);
        Assert.That(byName.Binding, Is.EqualTo(location.Binding), name);
    }
}
