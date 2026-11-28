// file "AutoExposureHistogram.slang" - module "AutoExposureHistogram"
//
// Builds a spatially weighted 256-bin log2 luminance histogram using
// LDS reduction before atomic accumulation into the global histogram buffer.

#language slang 2025
module AutoExposureHistogram;

import Core;
import SharedSampler;
import AlcoRendering_AutoExposure;

[GPURoot]
public struct AutoExposureHistogramRoot
{
    public GPUTextureId Source;
    public uint SampleSizeX;
    public uint SampleSizeY;
    public uint MeteringMode;
    public float2 InvSampleSize;
    public float MinLogLuminance;
    public float InvLogLuminanceRange;
    public GPUBufferView Histogram;
    public float CenterWeightRadius;
    public uint Pad0;
};

groupshared uint s_Histogram[AUTO_EXPOSURE_BIN_COUNT];

[shader("compute")]
[numthreads(AUTO_EXPOSURE_HISTOGRAM_GROUP_DIM, AUTO_EXPOSURE_HISTOGRAM_GROUP_DIM, 1)]
void mainCS(uint3 id : SV_DispatchThreadID, uint groupIndex : SV_GroupIndex)
{
    s_Histogram[groupIndex] = 0u;
    GroupMemoryBarrierWithGroupSync();

    AutoExposureHistogramRoot root = gpu_load_root<AutoExposureHistogramRoot>();
    if (id.x < root.SampleSizeX && id.y < root.SampleSizeY)
    {
        float2 uv = (float2(id.xy) + 0.5f) * root.InvSampleSize;
        uint weight = autoExposureMeteringWeight(uv, root.MeteringMode, root.CenterWeightRadius);
        if (weight > 0u)
        {
            Texture2D<float4> source = gpu_texture<Texture2D<float4>>(root.Source.index);
            float3 rgb = source.SampleLevel(shared_sampler_linear_clamp(), uv, 0.0f).rgb;
            if (!any(isnan(rgb)) && !any(isinf(rgb)))
            {
                float lum = autoExposureLuminance(max(rgb, 0.0f));
                uint bin = autoExposureLuminanceToBin(lum, root.MinLogLuminance, root.InvLogLuminanceRange);
                InterlockedAdd(s_Histogram[bin], weight);
            }
        }
    }

    GroupMemoryBarrierWithGroupSync();

    uint count = s_Histogram[groupIndex];
    if (count > 0u)
    {
        gpu_atomic_add_u32(gpu_offset_bytes(root.Histogram, groupIndex * 4u), count);
    }
}
