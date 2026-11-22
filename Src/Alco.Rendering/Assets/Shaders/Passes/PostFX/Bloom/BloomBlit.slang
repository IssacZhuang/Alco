// file "BloomBlit.slang" - module "BloomBlit"
//
// Fullscreen VB-less triangle composite pass supporting both in-place additive
// blending (bloom-only fetch onto loaded color target) and opaque fused composite
// (scene + bloom for separate/swapchain targets).

#language slang 2025
module BloomBlit;

import Core;
import SharedSampler;

public static const uint BLOOM_BLIT_SAMPLE_SCENE = 1u << 0;
public static const uint BLOOM_BLIT_SAMPLE_BLOOM = 1u << 1;

[GPURoot]
public struct BloomBlitRoot
{
    public GpuTextureId BloomTex;
    public GpuTextureId SceneTex;
    public uint Flags;
};

struct V2F
{
    float4 Position : SV_Position;
    float2 Uv : TEXCOORD0;
};

[shader("vertex")]
V2F MainVS(uint vertexIndex : SV_VertexID)
{
    float2 uv = float2((vertexIndex << 1) & 2, vertexIndex & 2);
    V2F output;
    output.Position = float4(uv * float2(2.0f, -2.0f) + float2(-1.0f, 1.0f), 0.0f, 1.0f);
    output.Uv = uv;
    return output;
}

[shader("pixel")]
float4 MainPS(V2F input) : SV_Target0
{
    BloomBlitRoot root = gpu_load_root<BloomBlitRoot>();
    SamplerState linearClamp = shared_sampler_linear_clamp();

    float3 rgb = float3(0.0f, 0.0f, 0.0f);
    float alpha = 1.0f;

    if ((root.Flags & BLOOM_BLIT_SAMPLE_SCENE) != 0u)
    {
        Texture2D<float4> scene = gpu_texture<Texture2D<float4>>(root.SceneTex.index);
        float4 sceneColor = scene.SampleLevel(linearClamp, input.Uv, 0.0f);
        rgb = sceneColor.rgb;
        alpha = sceneColor.a;
    }

    if ((root.Flags & BLOOM_BLIT_SAMPLE_BLOOM) != 0u)
    {
        Texture2D<float4> bloom = gpu_texture<Texture2D<float4>>(root.BloomTex.index);
        rgb += bloom.SampleLevel(linearClamp, input.Uv, 0.0f).rgb;
    }

    return float4(rgb, alpha);
}
