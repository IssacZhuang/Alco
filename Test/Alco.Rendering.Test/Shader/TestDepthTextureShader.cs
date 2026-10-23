using Alco.Graphics;
using Alco.Rendering;
using NUnit.Framework;

namespace Alco.Rendering.Test;

/// <summary>
/// Integration tests that slang reflection reports depth textures and comparison sampler bindings.
/// </summary>
[TestFixture]
public class TestDepthTextureShader
{
    private const string DepthShaderSource = """
        module test_depth_texture;

        cbuffer gbufferDepth : register(b0, space0)
        {
            DepthTexture2D gbufferDepth;
        };

        cbuffer shadow : register(b0, space1)
        {
            DepthTexture2D shadowMap;
            SamplerComparisonState shadowMapSampler;
        };

        struct V2F
        {
            float4 position : SV_POSITION;
            float2 uv : TEXCOORD0;
        };

        [shader("vertex")]
        V2F MainVS(float3 position : POSITION)
        {
            V2F output;
            output.position = float4(position, 1.0f);
            output.uv = position.xy;
            return output;
        }

        [shader("pixel")]
        float4 MainPS(V2F input) : SV_TARGET
        {
            float d = gbufferDepth.Load(int3(0, 0, 0));
            float s = shadowMap.SampleCmpLevelZero(shadowMapSampler, input.uv, 0.5);
            return float4(d, s, 0, 1);
        }
        """;

    private static ShaderModulesInfo Compile()
    {
        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem();
        using ShaderSystem shaderSystem = new(
            host.RenderingSystem, new SlangCompilerOptions { Resolver = _ => null }, cacheDirectory: null);
        Shader shader = shaderSystem.GetShaderFromModule(
            "test_depth_texture", "test_depth_texture.slang", DepthShaderSource);
        return shader.GetShaderModules();
    }

    private static BindGroupLayout GetGroup(ShaderModulesInfo modules, uint group)
    {
        foreach (BindGroupLayout layout in modules.ReflectionInfo.BindGroups)
        {
            if (layout.Group == group)
            {
                return layout;
            }
        }

        throw new AssertionException($"Bind group {group} not found");
    }

    /// <summary>Tests that a load-only depth texture is reflected with the depth sample type.</summary>
    [Test(Description = "A load-only DepthTexture2D is reflected with the Depth sample type")]
    public void CompileSlang_DepthReadTexture_ReflectedAsDepthSampleType()
    {
        ShaderModulesInfo modules = Compile();

        BindGroupLayout group = GetGroup(modules, 0);

        Assert.That(group.Bindings.Count, Is.EqualTo(1));
        Assert.That(group.Bindings[0].Entry.Type, Is.EqualTo(BindingType.Texture));
        Assert.That(group.Bindings[0].Entry.TextureInfo!.SampleType, Is.EqualTo(TextureSampleType.Depth));
    }

    /// <summary>Tests that a sampled depth texture is reflected with a depth texture and comparison sampler.</summary>
    [Test(Description = "A sampled DepthTexture2D pair is reflected as depth texture + comparison sampler")]
    public void CompileSlang_DepthSampledTexture_ReflectedAsDepthAndComparisonSampler()
    {
        ShaderModulesInfo modules = Compile();

        BindGroupLayout group = GetGroup(modules, 1);

        Assert.That(group.Bindings.Count, Is.EqualTo(2));
        Assert.That(group.Bindings[0].Entry.Type, Is.EqualTo(BindingType.Texture));
        Assert.That(group.Bindings[0].Entry.TextureInfo!.SampleType, Is.EqualTo(TextureSampleType.Depth));
        Assert.That(group.Bindings[1].Entry.Type, Is.EqualTo(BindingType.SamplerComparison));
    }
}
