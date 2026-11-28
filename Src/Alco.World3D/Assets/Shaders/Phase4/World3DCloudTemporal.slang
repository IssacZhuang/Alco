#language slang 2025
module World3DCloudTemporal;

import Core;
import SharedSampler;
import World3DSceneData;
import World3DCloudCommon;

[GPURoot]
public struct CloudTemporalRoot
{
    public GPUBufferView Views;
    public GPUBufferView CloudFrame;
    public GPUTextureId RawTex;
    public GPUTextureId HistoryInTex;
    public GPUTextureId DepthTex;
    public GPUTextureId HistoryOutTex;
    public uint TraceWidth;
    public uint TraceHeight;
    public float TemporalBlend;
    public uint Reserved0;
};

[shader("compute")]
[numthreads(8, 8, 1)]
public void csMain(uint3 id : SV_DispatchThreadID)
{
    CloudTemporalRoot root = gpu_load_root<CloudTemporalRoot>();
    if (id.x >= root.TraceWidth || id.y >= root.TraceHeight)
        return;

    RWTexture2D<float4> historyOut = gpu_texture<RWTexture2D<float4>>(root.HistoryOutTex.index);
    Texture2D<float4> rawTex = gpu_texture<Texture2D<float4>>(root.RawTex.index);
    int2 maxTrace = int2(int(root.TraceWidth) - 1, int(root.TraceHeight) - 1);
    float4 curr = rawTex.Load(int3(id.xy, 0));

    World3DCloudFrameGpu cfg = gpu_load<World3DCloudFrameGpu>(root.CloudFrame, 0);
    if ((cfg.Flags & WORLD3D_CLOUD_FLAG_ENABLED) == 0u
        || (cfg.Flags & WORLD3D_CLOUD_FLAG_HISTORY_VALID) == 0u
        || root.HistoryInTex.index == 0u)
    {
        historyOut[id.xy] = curr;
        return;
    }

    float4 sum = float4(0.0);
    float4 sumSq = float4(0.0);
    [unroll]
    for (int dy = -1; dy <= 1; ++dy)
    {
        [unroll]
        for (int dx = -1; dx <= 1; ++dx)
        {
            int2 coord = clamp(int2(id.xy) + int2(dx, dy), int2(0, 0), maxTrace);
            float4 s = rawTex.Load(int3(coord, 0));
            sum += s;
            sumSq += s * s;
        }
    }
    float4 mean = sum * (1.0 / 9.0);
    float4 variance = max(sumSq * (1.0 / 9.0) - mean * mean, float4(0.0));
    float4 stddev = sqrt(variance);
    float4 clampMin = mean - stddev * 1.35;
    float4 clampMax = mean + stddev * 1.35;

    World3DView currView = gpu_load<World3DView>(root.Views, 0);
    World3DView prevView = gpu_load<World3DView>(root.Views, 1);

    float2 uv = (float2(id.xy) + 0.5) / float2(float(root.TraceWidth), float(root.TraceHeight));
    float3 ray = cloud_reconstruct_ray(currView, uv);

    Texture2D<float> depthTex = gpu_texture<Texture2D<float>>(root.DepthTex.index);
    int2 fullDims = max(int2(currView.ViewportSizeInvSize.xy), int2(1, 1));
    int2 depthPixel = clamp(int2(uv * float2(fullDims)), int2(0, 0), fullDims - int2(1, 1));
    float depth = depthTex.Load(int3(depthPixel, 0));

    float3 eyeAnchorRel;
    if (depth > 0.0)
    {
        float2 ndc = uv * float2(2.0, -2.0) + float2(-1.0, 1.0);
        float4 eyeH = mul(float4(ndc, depth, 1.0), currView.CameraInvVP);
        eyeAnchorRel = eyeH.w > 1e-6 ? (eyeH.xyz / eyeH.w) : (ray * 5000.0);
    }
    else
    {
        float cloudMidAlt = 0.5 * (cfg.LayerParams.x + cfg.LayerParams.y);
        float slabDist = abs(ray.z) > 0.02
            ? clamp((cloudMidAlt - cfg.BasePhaseAndAlt.w) / ray.z, 500.0, cfg.ScatteringParams.w)
            : cfg.ScatteringParams.w;
        eyeAnchorRel = ray * slabDist;
    }

    float3 cellShift = float3(currView.EyeCell.xyz - prevView.EyeCell.xyz) * WORLD3D_CELL_SIZE
        + (currView.EyeLocalNear.xyz - prevView.EyeLocalNear.xyz);
    float3 prevEyeRel = eyeAnchorRel + cellShift;
    float4 prevClip = mul(float4(prevEyeRel, 1.0), prevView.CameraVP);
    if (prevClip.w <= 1e-5)
    {
        historyOut[id.xy] = curr;
        return;
    }

    float2 prevNdc = prevClip.xy / prevClip.w;
    float2 prevUv = prevNdc * float2(0.5, -0.5) + float2(0.5, 0.5);
    if (prevUv.x <= 0.0 || prevUv.x >= 1.0 || prevUv.y <= 0.0 || prevUv.y >= 1.0)
    {
        historyOut[id.xy] = curr;
        return;
    }

    Texture2D<float4> historyIn = gpu_texture<Texture2D<float4>>(root.HistoryInTex.index);
    float4 prev = historyIn.SampleLevel(shared_sampler_linear_clamp(), prevUv, 0.0);
    float4 clampedPrev = clamp(prev, clampMin, clampMax);
    historyOut[id.xy] = lerp(curr, clampedPrev, saturate(root.TemporalBlend));
}
