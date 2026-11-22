// file "BloomSetup.slang" - module "BloomSetup"
//
// Bloom setup compute pass: extracts light above a soft linear ramp threshold
// (luma = dot(rgb, (0.3, 0.59, 0.11))) into the half-resolution level-0 texture,
// filtering NaN/Inf and negative values. Negative threshold disables filtering.

#language slang 2025
module BloomSetup;

import Core;
import SharedSampler;
import AlcoRendering_Bloom;

[GPURoot]
public struct BloomSetupRoot
{
    public GpuTextureId Source;
    public GpuTextureId Dest;
    public float2 InvDestSize;
    public uint2 DestSize;
    public float Threshold;
};

[shader("compute")]
[numthreads(BLOOM_SETUP_THREADS, BLOOM_SETUP_THREADS, 1)]
void mainCS(uint3 id : SV_DispatchThreadID)
{
    BloomSetupRoot root = gpu_load_root<BloomSetupRoot>();
    if (id.x >= root.DestSize.x || id.y >= root.DestSize.y)
    {
        return;
    }

    float2 uv = (float2(id.xy) + 0.5f) * root.InvDestSize;
    Texture2D<float4> source = gpu_texture<Texture2D<float4>>(root.Source.index);
    float4 color = source.SampleLevel(shared_sampler_linear_clamp(), uv, 0.0f);

    RWTexture2D<float4> dest = gpu_texture<RWTexture2D<float4>>(root.Dest.index);
    if (any(isnan(color.rgb)) || any(isinf(color.rgb)))
    {
        dest[id.xy] = float4(0.0f, 0.0f, 0.0f, 1.0f);
        return;
    }

    float3 linearColor = max(color.rgb, 0.0f);
    float amount = 1.0f;
    if (root.Threshold >= 0.0f)
    {
        float luma = dot(linearColor, float3(0.3f, 0.59f, 0.11f));
        amount = saturate((luma - root.Threshold) * 0.5f);
    }

    dest[id.xy] = float4(linearColor * amount, 1.0f);
}
