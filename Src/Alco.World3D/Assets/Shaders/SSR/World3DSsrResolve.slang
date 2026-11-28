#language slang 2025
module World3DSsrResolve;

import Core;
import SharedSampler;
import World3DSceneData;

[GPURoot]
public struct SsrResolveRoot
{
    public GPUBufferView Views;
    public GPUTextureId RawColorTex;
    public GPUTextureId MetaTex;
    public GPUTextureId NormalTex;
    public GPUTextureId MaterialTex;
    public GPUTextureId DepthTex;
    public GPUTextureId HistoryInTex;
    public GPUTextureId HistoryOutTex;
    public uint TraceWidth;
    public uint TraceHeight;
    public float TemporalBlend;
    public uint HistoryValid;
};

[shader("compute")]
[numthreads(8, 8, 1)]
public void csMain(uint3 id : SV_DispatchThreadID)
{
    SsrResolveRoot root = gpu_load_root<SsrResolveRoot>();
    if (id.x >= root.TraceWidth || id.y >= root.TraceHeight)
        return;

    RWTexture2D<float4> historyOut = gpu_texture<RWTexture2D<float4>>(root.HistoryOutTex.index);
    Texture2D<float4> rawTex = gpu_texture<Texture2D<float4>>(root.RawColorTex.index);
    Texture2D<float4> metaTex = gpu_texture<Texture2D<float4>>(root.MetaTex.index);
    Texture2D<float4> normalTex = gpu_texture<Texture2D<float4>>(root.NormalTex.index);
    Texture2D<float> depthTex = gpu_texture<Texture2D<float>>(root.DepthTex.index);

    World3DView currView = gpu_load<World3DView>(root.Views, 0u);
    int2 fullDims = max(int2(currView.ViewportSizeInvSize.xy), int2(1, 1));
    int2 maxFull = fullDims - int2(1, 1);
    int2 maxTrace = int2(int(root.TraceWidth) - 1, int(root.TraceHeight) - 1);

    float2 uv = (float2(id.xy) + 0.5) / float2(float(root.TraceWidth), float(root.TraceHeight));
    int2 centerFull = clamp(int2(uv * float2(fullDims)), int2(0, 0), maxFull);
    float centerDepth = depthTex.Load(int3(centerFull, 0));
    if (centerDepth <= 0.0)
    {
        historyOut[id.xy] = float4(0.0);
        return;
    }

    float2 centerNdc = uv * float2(2.0, -2.0) + float2(-1.0, 1.0);
    float4 centerEyeH = mul(float4(centerNdc, centerDepth, 1.0), currView.CameraInvVP);
    float3 centerPos = centerEyeH.xyz / max(centerEyeH.w, 1e-6);
    float centerDist = length(centerPos);
    float3 centerNormal = normalize(normalTex.Load(int3(centerFull, 0)).xyz * 2.0 - 1.0);

    float4 centerRaw = rawTex.Load(int3(id.xy, 0));
    float4 centerMeta = metaTex.Load(int3(id.xy, 0));
    float roughness = centerMeta.w;

    float4 spatialSum = centerRaw;
    float weightSum = 1.0;
    float4 mom1 = centerRaw;
    float4 mom2 = centerRaw * centerRaw;
    float momCount = 1.0;

    float planeScale = rcp(max(centerDist * 0.025, 0.02));
    int filterRadius = roughness < 0.08 ? 1 : 2;
    for (int dy = -2; dy <= 2; ++dy)
    {
        for (int dx = -2; dx <= 2; ++dx)
        {
            if ((dx == 0 && dy == 0) || abs(dx) > filterRadius || abs(dy) > filterRadius)
                continue;

            int2 tc = clamp(int2(id.xy) + int2(dx, dy), int2(0, 0), maxTrace);
            float4 s = rawTex.Load(int3(tc, 0));
            float2 nbUv = (float2(tc) + 0.5) / float2(float(root.TraceWidth), float(root.TraceHeight));
            int2 nbFull = clamp(int2(nbUv * float2(fullDims)), int2(0, 0), maxFull);
            float nbDepth = depthTex.Load(int3(nbFull, 0));
            if (nbDepth <= 0.0)
                continue;

            float2 nbNdc = nbUv * float2(2.0, -2.0) + float2(-1.0, 1.0);
            float4 nbEyeH = mul(float4(nbNdc, nbDepth, 1.0), currView.CameraInvVP);
            float3 nbPos = nbEyeH.xyz / max(nbEyeH.w, 1e-6);
            float3 nbNormal = normalize(normalTex.Load(int3(nbFull, 0)).xyz * 2.0 - 1.0);

            float planeDiff = abs(dot(nbPos - centerPos, centerNormal)) * planeScale;
            float normW = pow(saturate(dot(centerNormal, nbNormal)), 8.0);
            float spatialW = exp(-float(dx * dx + dy * dy) * 0.25 - planeDiff * 2.0) * normW;
            if (s.a > 0.001)
            {
                spatialSum += s * spatialW;
                weightSum += spatialW;
            }
            mom1 += s;
            mom2 += s * s;
            momCount += 1.0;
        }
    }

    float4 spatial = spatialSum / max(weightSum, 1e-4);
    if (root.HistoryValid == 0u || root.HistoryInTex.index == 0u)
    {
        historyOut[id.xy] = spatial;
        return;
    }

    World3DView prevView = gpu_load<World3DView>(root.Views, 1u);
    float3 reflDir = reflect(normalize(centerPos), centerNormal);
    float3 virtualPos = centerPos + reflDir * (centerMeta.z * lerp(0.85, 0.15, saturate(roughness * 1.5)));
    float3 cellShift = float3(currView.EyeCell.xyz - prevView.EyeCell.xyz) * WORLD3D_CELL_SIZE
        + (currView.EyeLocalNear.xyz - prevView.EyeLocalNear.xyz);

    float4 prevClip = mul(float4(virtualPos + cellShift, 1.0), prevView.CameraVP);
    float4 surfPrevClip = mul(float4(centerPos + cellShift, 1.0), prevView.CameraVP);
    // Blend surface vs virtual reprojection smoothly so grazing reflections stay crisp while steep angles track parallax.
    float2 vUv = prevClip.w > 1e-5 ? ((prevClip.xy / prevClip.w) * float2(0.5, -0.5) + float2(0.5, 0.5)) : uv;
    float2 sUv = surfPrevClip.w > 1e-5 ? ((surfPrevClip.xy / surfPrevClip.w) * float2(0.5, -0.5) + float2(0.5, 0.5)) : uv;
    float2 prevUv = lerp(vUv, sUv, saturate(roughness * 1.8));
    if (prevClip.w <= 1e-5 || prevUv.x <= 0.0 || prevUv.x >= 1.0 || prevUv.y <= 0.0 || prevUv.y >= 1.0)
    {
        historyOut[id.xy] = spatial;
        return;
    }

    float4 mean = mom1 / momCount;
    float4 stddev = sqrt(max(mom2 / momCount - mean * mean, float4(0.0)));
    float4 minClamp = mean - stddev * 1.5;
    float4 maxClamp = mean + stddev * 1.5;

    Texture2D<float4> histIn = gpu_texture<Texture2D<float4>>(root.HistoryInTex.index);
    float4 prev = clamp(histIn.SampleLevel(shared_sampler_linear_clamp(), prevUv, 0.0), minClamp, maxClamp);
    float blend = root.TemporalBlend * (1.0 - saturate(roughness) * 0.35);
    historyOut[id.xy] = lerp(spatial, prev, saturate(blend));
}
