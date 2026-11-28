#language slang 2025
module World3DVolumetricLightComposite;

import Core;
import SharedSampler;
import World3DSceneData;
import World3DCloudCommon;

[GPURoot]
public struct VolumetricLightCompositeRoot
{
    public GPUBufferView Views;
    public GPUTextureId HdrInTex;
    public GPUTextureId ScatterTex;
    public GPUTextureId DepthTex;
    public GPUTextureId HdrOutTex;
    public uint TraceWidth;
    public uint TraceHeight;
    public uint HalfResolution;
    public uint Reserved0;
};

float4 bilateralUpsampleVolLight(
    Texture2D<float4> scatterTex,
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
                depthW = td <= 0.0 ? 1.0 : 0.05;
            }
            else if (td <= 0.0)
            {
                depthW = 0.05;
            }
            else
            {
                float tLinearZ = cloud_linear_view_z(view, td);
                float relDiff = abs(tLinearZ - centerLinearZ) / max(centerLinearZ * 0.2, 0.5);
                depthW = exp(-relDiff * 2.5);
            }

            float w = spatialW * depthW;
            accum += scatterTex.Load(int3(tc, 0)) * w;
            weightSum += w;
        }
    }

    if (weightSum > 1e-5)
        return accum / weightSum;
    return scatterTex.SampleLevel(shared_sampler_linear_clamp(), uv, 0.0);
}

[shader("compute")]
[numthreads(8, 8, 1)]
public void csMain(uint3 id : SV_DispatchThreadID)
{
    VolumetricLightCompositeRoot root = gpu_load_root<VolumetricLightCompositeRoot>();
    World3DView view = gpu_load<World3DView>(root.Views, 0);
    uint2 fullDims = uint2(view.ViewportSizeInvSize.xy);
    if (any(id.xy >= fullDims))
        return;

    Texture2D<float4> hdrIn = gpu_texture<Texture2D<float4>>(root.HdrInTex.index);
    Texture2D<float4> scatterTex = gpu_texture<Texture2D<float4>>(root.ScatterTex.index);
    RWTexture2D<float4> hdrOut = gpu_texture<RWTexture2D<float4>>(root.HdrOutTex.index);

    float4 baseHdr = hdrIn.Load(int3(id.xy, 0));
    float4 vol;
    if (root.HalfResolution == 0u)
    {
        vol = scatterTex.Load(int3(id.xy, 0));
    }
    else
    {
        Texture2D<float> depthTex = gpu_texture<Texture2D<float>>(root.DepthTex.index);
        float depth = depthTex.Load(int3(id.xy, 0));
        float2 uv = (float2(id.xy) + 0.5) * view.ViewportSizeInvSize.zw;
        vol = bilateralUpsampleVolLight(
            scatterTex, depthTex, view, uint2(root.TraceWidth, root.TraceHeight), int2(fullDims), uv, depth);
    }

    hdrOut[id.xy] = float4(baseHdr.rgb * vol.a + vol.rgb, 1.0);
}
