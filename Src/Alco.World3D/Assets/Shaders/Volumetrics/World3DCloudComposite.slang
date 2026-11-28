#language slang 2025
module World3DCloudComposite;

import Core;
import SharedSampler;
import World3DSceneData;
import World3DShadowSample;
import World3DCloudCommon;

[GPURoot]
public struct CloudCompositeRoot
{
    public GPUBufferView Views;
    public GPUBufferView ShadowFrame;
    public GPUBufferView CloudFrame;
    public GPUTextureId HdrInTex;
    public GPUTextureId ResolvedCloudsTex;
    public GPUTextureId DepthTex;
    public GPUTextureId HdrOutTex;
    public uint TraceWidth;
    public uint TraceHeight;
};

float4 bilateralUpsampleClouds(
    Texture2D<float4> cloudTex,
    Texture2D<float> depthTex,
    World3DView view,
    uint2 traceDims,
    int2 fullDims,
    float2 uv,
    float centerDepth)
{
    bool centerSky = centerDepth <= 0.0;
    float centerLinearZ = centerSky ? 1.0e8 : cloud_linear_view_z(view, centerDepth);

    float2 tracePos = uv * float2(traceDims) - float2(0.5);
    int2 baseCoord = int2(floor(tracePos));
    int2 maxTrace = int2(traceDims) - int2(1, 1);
    int2 maxFull = fullDims - int2(1, 1);

    float4 accum = float4(0.0);
    float weightSum = 0.0;
    [unroll]
    for (int dy = -1; dy <= 1; ++dy)
    {
        [unroll]
        for (int dx = -1; dx <= 1; ++dx)
        {
            int2 tc = clamp(baseCoord + int2(dx, dy), int2(0, 0), maxTrace);
            float2 tcUv = (float2(tc) + 0.5) / float2(traceDims);
            int2 sampleFull = clamp(int2(tcUv * float2(fullDims)), int2(0, 0), maxFull);
            float td = depthTex.Load(int3(sampleFull, 0));

            float2 delta = float2(tc) - tracePos;
            float spatialW = exp(-dot(delta, delta) * 1.15);
            float depthW;
            if (centerSky)
            {
                depthW = (td <= 0.0) ? 1.0 : 0.01;
            }
            else if (td <= 0.0)
            {
                depthW = 0.02;
            }
            else
            {
                float tLinearZ = cloud_linear_view_z(view, td);
                float relDiff = abs(tLinearZ - centerLinearZ) / max(centerLinearZ * 0.15, 1.0);
                depthW = exp(-relDiff * 2.5);
            }

            float w = spatialW * depthW;
            accum += cloudTex.Load(int3(tc, 0)) * w;
            weightSum += w;
        }
    }

    if (weightSum > 1e-5)
        return accum / weightSum;
    return cloudTex.SampleLevel(shared_sampler_linear_clamp(), uv, 0.0);
}

[shader("compute")]
[numthreads(8, 8, 1)]
public void csMain(uint3 id : SV_DispatchThreadID)
{
    CloudCompositeRoot root = gpu_load_root<CloudCompositeRoot>();
    World3DView view = gpu_load<World3DView>(root.Views, 0);
    uint2 fullDims = uint2(view.ViewportSizeInvSize.xy);
    if (any(id.xy >= fullDims))
        return;

    Texture2D<float4> hdrIn = gpu_texture<Texture2D<float4>>(root.HdrInTex.index);
    RWTexture2D<float4> hdrOut = gpu_texture<RWTexture2D<float4>>(root.HdrOutTex.index);
    float4 baseHdr = hdrIn.Load(int3(id.xy, 0));

    World3DShadowFrameGpu shadowFrame = gpu_load<World3DShadowFrameGpu>(root.ShadowFrame, 0);
    Texture2D<float> depthTex = gpu_texture<Texture2D<float>>(root.DepthTex.index);
    Texture2D<float4> cloudTex = gpu_texture<Texture2D<float4>>(root.ResolvedCloudsTex.index);

    float depth = depthTex.Load(int3(id.xy, 0));
    float2 uv = (float2(id.xy) + 0.5) * view.ViewportSizeInvSize.zw;
    World3DCloudFrameGpu cfg = gpu_load<World3DCloudFrameGpu>(root.CloudFrame, 0);
    float4 cloud = float4(0.0, 0.0, 0.0, 1.0);
    if (depth <= 0.0)
    {
        cloud = bilateralUpsampleClouds(
            cloudTex, depthTex, view, uint2(root.TraceWidth, root.TraceHeight), int2(fullDims), uv, depth);
    }
    else
    {
        float2 ndc = uv * float2(2.0, -2.0) + float2(-1.0, 1.0);
        float4 eyeH = mul(float4(ndc, depth, 1.0), view.CameraInvVP);
        float3 eyeRelPos = eyeH.xyz / max(eyeH.w, 1e-6);
        float sceneDist = length(eyeRelPos);
        float3 ray = sceneDist > 1e-5 ? (eyeRelPos / sceneDist) : cloud_reconstruct_ray(view, uv);
        float tEnter = 0.0;
        float tExit = 0.0;
        if (cloud_ray_slab(cfg.BasePhaseAndAlt.w, ray.z, cfg.LayerParams.x, cfg.LayerParams.y, sceneDist, tEnter, tExit))
        {
            cloud = bilateralUpsampleClouds(
                cloudTex, depthTex, view, uint2(root.TraceWidth, root.TraceHeight), int2(fullDims), uv, depth);
        }
    }

    if (shadowFrame.DebugView == 11u)
    {
        if (depth <= 0.0)
        {
            float cloudOpacity = saturate(1.0 - cloud.a);
            hdrOut[id.xy] = float4(cloudOpacity.xxx, 1.0);
        }
        else
        {
            float2 ndc = uv * float2(2.0, -2.0) + float2(-1.0, 1.0);
            float4 eyeH = mul(float4(ndc, depth, 1.0), view.CameraInvVP);
            float3 eyeRelPos = eyeH.xyz / max(eyeH.w, 1e-6);
            float cShadow = world3d_sample_cloud_shadow(shadowFrame, view, eyeRelPos);
            hdrOut[id.xy] = float4(lerp(float3(0.08, 0.15, 0.35), float3(1.0, 0.92, 0.72), cShadow), 1.0);
        }
        return;
    }

    float3 composited = baseHdr.rgb * cloud.a + cloud.rgb;
    hdrOut[id.xy] = float4(composited, 1.0);
}
