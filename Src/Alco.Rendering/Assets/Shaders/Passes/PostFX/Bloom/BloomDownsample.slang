// file "BloomDownsample.slang" - module "BloomDownsample"
//
// Bloom 4-tap bilinear downsample compute pass averaging to a 4x4 box footprint.
// Accepts separate InvSourceSize and InvDestSize to preserve exact odd-dimension UV parity.

#language slang 2025
module BloomDownsample;

import Core;
import SharedSampler;
import AlcoRendering_Bloom;

[GPURoot]
public struct BloomDownsampleRoot
{
    public GPUTextureId Source;
    public GPUTextureId Dest;
    public float2 InvSourceSize;
    public float2 InvDestSize;
    public uint2 DestSize;
};

[shader("compute")]
[numthreads(BLOOM_DOWNSAMPLE_THREADS, BLOOM_DOWNSAMPLE_THREADS, 1)]
void mainCS(uint3 id : SV_DispatchThreadID)
{
    BloomDownsampleRoot root = gpu_load_root<BloomDownsampleRoot>();
    if (id.x >= root.DestSize.x || id.y >= root.DestSize.y)
    {
        return;
    }

    float2 uv = (float2(id.xy) + 0.5f) * root.InvDestSize;
    float2 texel = root.InvSourceSize;
    Texture2D<float4> source = gpu_texture<Texture2D<float4>>(root.Source.index);
    SamplerState linearClamp = shared_sampler_linear_clamp();

    float3 color = source.SampleLevel(linearClamp, uv + texel * float2(-1.0f, -1.0f), 0.0f).rgb;
    color += source.SampleLevel(linearClamp, uv + texel * float2(1.0f, -1.0f), 0.0f).rgb;
    color += source.SampleLevel(linearClamp, uv + texel * float2(-1.0f, 1.0f), 0.0f).rgb;
    color += source.SampleLevel(linearClamp, uv + texel * float2(1.0f, 1.0f), 0.0f).rgb;

    RWTexture2D<float4> dest = gpu_texture<RWTexture2D<float4>>(root.Dest.index);
    dest[id.xy] = float4(max(color * 0.25f, 0.0f), 1.0f);
}
