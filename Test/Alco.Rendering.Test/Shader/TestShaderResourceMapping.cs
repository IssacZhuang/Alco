using Alco.Graphics;
using Alco.Rendering;
using Alco.ShaderCompiler;
using NUnit.Framework;

namespace Alco.Rendering.Test;

/// <summary>
/// Tests for the dense resource mapping of <see cref="ShaderReflectionInfo"/> on the
/// slang module path: one resource id per settable shader variable (buffer or
/// texture), with sampler companion entries excluded, exercised through a real
/// slang compilation.
/// </summary>
[TestFixture]
public class TestShaderResourceMapping
{
    // A mixed block (uniform data plus resource members, one set), then one set
    // per remaining resource, with a sampler paired to _albedo by the engine's
    // name##Sampler convention.
    private const string PackedShader = """
        module packed_shader;

        cbuffer _data : register(b0, space0)
        {
            float4 value;
            Texture2D _albedo;
        };

        cbuffer _albedoSampler : register(b0, space1)
        {
            SamplerState _albedoSampler;
        };

        cbuffer _buffer : register(b0, space2)
        {
            RWStructuredBuffer<float> _buffer;
        };

        cbuffer _storage : register(b0, space3)
        {
            [[vk::image_format("rgba16f")]] RWTexture2D<float4> _storage;
        };

        [shader("compute")]
        [numthreads(1, 1, 1)]
        void MainCS(uint3 id : SV_DispatchThreadID)
        {
            _storage[id.xy] = _albedo.SampleLevel(_albedoSampler, float2(0, 0), 0) + value + _buffer[0];
        }
        """;

    [Test(Description = "Settable resources get dense ids in reflection order; sampler companions are excluded")]
    public void Reflection_DenseResourceMapping_ExcludesCompanions()
    {
        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem();
        using ShaderSystem shaderSystem = new(
            host.RenderingSystem, new SlangCompilerOptions { Resolver = _ => null }, cacheDirectory: null);
        Shader shader = shaderSystem.GetShaderFromModule("packed_shader", "packed_shader.slang", PackedShader);
        ShaderReflectionInfo info = shader.GetShaderModules().ReflectionInfo;

        Assert.That(info.BindGroups.Count, Is.EqualTo(4));
        Assert.That(info.ResourceCount, Is.EqualTo(4));

        // Sampler companions are not settable resources.
        Assert.IsFalse(info.TryGetResourceId("_albedoSampler", out _));

        // Id and name lookups agree with the resource locations.
        AssertResource(info, 0, "_data", 0, 0, BindingType.UniformBuffer);
        AssertResource(info, 1, "_albedo", 0, 1, BindingType.Texture);
        AssertResource(info, 2, "_buffer", 2, 0, BindingType.StorageBuffer);
        AssertResource(info, 3, "_storage", 3, 0, BindingType.StorageTexture);
    }

    private static void AssertResource(ShaderReflectionInfo info, uint id, string name, int group, uint binding, BindingType type)
    {
        Assert.IsTrue(info.TryGetResourceId(name, out uint actualId), name);
        Assert.That(actualId, Is.EqualTo(id), name);
        Assert.That(info.GetResourceName(id), Is.EqualTo(name), name);

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
