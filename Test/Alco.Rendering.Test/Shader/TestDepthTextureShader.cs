using NUnit.Framework;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.Rendering.Test;

/// <summary>
/// Integration tests for depth texture handling in <see cref="ShaderUtility.CompileHLSL"/>:
/// shaders declare depth textures with the DEFINE_TEX2D_DEPTH / DEFINE_TEX2D_DEPTH_SAMPLE
/// macros, the compiled SPIR-V is rewritten to depth images (SpirvDepthTexturePatcher)
/// and the reflection must report the depth sample type and comparison sampler binding.
/// </summary>
[TestFixture]
public class TestDepthTextureShader
{
    private const string DepthShaderText = @"
#define vk_binding vk::binding
#define SLOT(set, bind) [[vk_binding(bind, set)]]
#define DEFINE_TEX2D_DEPTH(index, name) SLOT(index, 0) Texture2D<float> name
#define DEFINE_TEX2D_DEPTH_SAMPLE(index, name) SLOT(index, 0) Texture2D<float> name; SLOT(index, 1) SamplerComparisonState name##Sampler

DEFINE_TEX2D_DEPTH(0, _gbufferDepth);
DEFINE_TEX2D_DEPTH_SAMPLE(1, _shadowMap);

struct V2F
{
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
};

[shader(""vertex"")]
V2F MainVS(float3 position : POSITION)
{
    V2F output;
    output.position = float4(position, 1.0f);
    output.uv = position.xy;
    return output;
}

[shader(""pixel"")]
float4 MainPS(V2F input) : SV_TARGET
{
    float d = _gbufferDepth.Load(int3(0, 0, 0));
    float s = _shadowMap.SampleCmpLevelZero(_shadowMapSampler, input.uv, 0.5);
    return float4(d, s, 0, 1);
}
";

    private static ShaderModulesInfo Compile()
    {
        return ShaderUtility.CompileHLSL(DepthShaderText, "test_depth.hlsl", ReadOnlySpan<string>.Empty, 8);
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

    [Test(Description = "A DEFINE_TEX2D_DEPTH texture is reflected with the Depth sample type")]
    public void CompileHLSL_DepthReadTexture_ReflectedAsDepthSampleType()
    {
        ShaderModulesInfo modules = Compile();

        BindGroupLayout group = GetGroup(modules, 0);

        Assert.That(group.Bindings.Count, Is.EqualTo(1));
        Assert.That(group.Bindings[0].Entry.Type, Is.EqualTo(BindingType.Texture));
        Assert.That(group.Bindings[0].Entry.TextureInfo.SampleType, Is.EqualTo(TextureSampleType.Depth));
    }

    [Test(Description = "A DEFINE_TEX2D_DEPTH_SAMPLE pair is reflected as depth texture + comparison sampler")]
    public void CompileHLSL_DepthSampledTexture_ReflectedAsDepthAndComparisonSampler()
    {
        ShaderModulesInfo modules = Compile();

        BindGroupLayout group = GetGroup(modules, 1);

        Assert.That(group.Bindings.Count, Is.EqualTo(2));
        Assert.That(group.Bindings[0].Entry.Type, Is.EqualTo(BindingType.Texture));
        Assert.That(group.Bindings[0].Entry.TextureInfo.SampleType, Is.EqualTo(TextureSampleType.Depth));
        Assert.That(group.Bindings[1].Entry.Type, Is.EqualTo(BindingType.SamplerComparison));
    }

    [Test(Description = "The depth comparison group classifies as a texture-sampler group for material binding")]
    public void CompileHLSL_DepthSampledTexture_ClassifiedAsTextureSamplerGroup()
    {
        ShaderModulesInfo modules = Compile();

        Assert.That(MaterialUtility.IsTextureReadGroup(GetGroup(modules, 0).Bindings), Is.True);
        Assert.That(MaterialUtility.IsTextureSamplerGroup(GetGroup(modules, 1).Bindings), Is.True);
    }
}
