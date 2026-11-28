// file "TonemapCompute.slang" - module "TonemapCompute"
//
// Single-pass bindless compute tone-mapping pass supporting Linear passthrough,
// ACES fitted, and Khronos PBR Neutral via compile-time value specializations.

#language slang 2025
module TonemapCompute;

import Core;
import SharedSampler;
import AlcoRendering_AutoExposure;
import AlcoRendering_Tonemap;

[GPURoot]
public struct TonemapRoot
{
    public GPUTextureId Source;
    public GPUTextureId Dest;
    public uint2 DestSize;
    public float2 InvDestSize;
    public float Exposure;
    public float Gamma;
    public float AcesA;
    public float AcesB;
    public float AcesC;
    public float AcesD;
    public float AcesE;
    public float NeutralStartCompression;
    public float NeutralDesaturation;
    public uint Pad0;
    public GPUBufferView ExposureState;
};

[shader("compute")]
[numthreads(TONEMAP_THREAD_GROUP_DIM, TONEMAP_THREAD_GROUP_DIM, 1)]
void mainCS<let Operator : int, let EnableDither : int, let UseAutoExposure : int>(uint3 id : SV_DispatchThreadID)
{
    TonemapRoot root = gpu_load_root<TonemapRoot>();
    if (id.x >= root.DestSize.x || id.y >= root.DestSize.y)
    {
        return;
    }

    float2 pixelPos = float2(id.xy) + 0.5f;
    float2 uv = pixelPos * root.InvDestSize;
    Texture2D<float4> source = gpu_texture<Texture2D<float4>>(root.Source.index);
    float4 sampled = source.SampleLevel(shared_sampler_linear_clamp(), uv, 0.0f);

    float exposure = root.Exposure;
    if (UseAutoExposure != 0 && root.ExposureState.Index != 0u)
    {
        float autoExp = gpu_load<AutoExposureState>(root.ExposureState, 0u).Exposure;
        if (isfinite(autoExp) && autoExp > 0.0f)
        {
            exposure *= autoExp;
        }
    }

    TonemapParams params;
    params.Exposure = exposure;
    params.Gamma = root.Gamma;
    params.AcesA = root.AcesA;
    params.AcesB = root.AcesB;
    params.AcesC = root.AcesC;
    params.AcesD = root.AcesD;
    params.AcesE = root.AcesE;
    params.NeutralStartCompression = root.NeutralStartCompression;
    params.NeutralDesaturation = root.NeutralDesaturation;

    RWTexture2D<float4> dest = gpu_texture<RWTexture2D<float4>>(root.Dest.index);
    dest[id.xy] = tonemapEvaluate<Operator, EnableDither>(sampled, pixelPos, params);
}
