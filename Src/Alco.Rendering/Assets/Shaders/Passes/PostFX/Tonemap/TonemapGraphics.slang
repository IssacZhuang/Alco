// file "TonemapGraphics.slang" - module "TonemapGraphics"
//
// Single-pass VB-less fullscreen triangle tone-mapping pass for swapchain and
// non-storage render targets, sharing the AlcoRendering_Tonemap core algorithm.

#language slang 2025
module TonemapGraphics;

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
float4 MainPS<let Operator : int, let EnableDither : int, let UseAutoExposure : int>(V2F input) : SV_Target0
{
    TonemapRoot root = gpu_load_root<TonemapRoot>();
    Texture2D<float4> source = gpu_texture<Texture2D<float4>>(root.Source.index);
    float4 sampled = source.SampleLevel(shared_sampler_linear_clamp(), input.Uv, 0.0f);

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

    return tonemapEvaluate<Operator, EnableDither>(sampled, input.Position.xy, params);
}
