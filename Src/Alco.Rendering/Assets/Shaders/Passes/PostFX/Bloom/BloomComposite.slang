// file "BloomComposite.slang" - module "BloomComposite"
//
// Fused compute composite pass combining full-resolution scene input with the
// half-resolution accumulated bloom texture into a separate storage output.

#language slang 2025
module BloomComposite;

import Core;
import SharedSampler;
import AlcoRendering_Bloom;

[GPURoot]
public struct BloomCompositeRoot
{
    public GpuTextureId SceneTex;
    public GpuTextureId BloomTex;
    public GpuTextureId Dest;
    public float2 InvDestSize;
    public uint2 DestSize;
    public uint HasBloom;
};

[shader("compute")]
[numthreads(BLOOM_SETUP_THREADS, BLOOM_SETUP_THREADS, 1)]
void mainCS(uint3 id : SV_DispatchThreadID)
{
    BloomCompositeRoot root = gpu_load_root<BloomCompositeRoot>();
    if (id.x >= root.DestSize.x || id.y >= root.DestSize.y)
    {
        return;
    }

    SamplerState linearClamp = shared_sampler_linear_clamp();
    float2 uv = (float2(id.xy) + 0.5f) * root.InvDestSize;

    Texture2D<float4> scene = gpu_texture<Texture2D<float4>>(root.SceneTex.index);
    float4 color = scene.SampleLevel(linearClamp, uv, 0.0f);

    if (root.HasBloom != 0u)
    {
        Texture2D<float4> bloom = gpu_texture<Texture2D<float4>>(root.BloomTex.index);
        color.rgb += bloom.SampleLevel(linearClamp, uv, 0.0f).rgb;
    }

    RWTexture2D<float4> dest = gpu_texture<RWTexture2D<float4>>(root.Dest.index);
    dest[id.xy] = color;
}
