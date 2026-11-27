#language slang 2025
module World3DVoxelDemosaic;

import Core;
import SharedSampler;
import World3DSceneData;

[GPURoot]
public struct VoxelDemosaicRoot
{
    public GPUBufferView Views; // [0] = current, [1] = previous
    public GPUTextureId DepthTex;
    public GPUTextureId NormalTex;
    public GPUTextureId RawDiffuseTex;
    public GPUTextureId RawSpecularTex;
    public GPUTextureId PrevHistoryDiffuseTex;
    public GPUTextureId PrevHistorySpecularTex;
    public GPUTextureId OutResolvedDiffuseTex;
    public GPUTextureId OutResolvedSpecularTex;
    public GPUTextureId OutHistoryDiffuseTex;
    public GPUTextureId OutHistorySpecularTex;
    public uint TraceWidth;
    public uint TraceHeight;
    public uint FullWidth;
    public uint FullHeight;
    public uint HalfResMode;
    public uint FrameIndex;
    public uint HasHistory;
    public float TemporalBlendAlpha;
};

uint2 traceCoordToFullPixel(uint2 traceCoord, uint frameIndex, uint halfRes, uint2 fullSize)
{
    if (halfRes == 0u)
        return min(traceCoord, fullSize - 1u);
    return min(traceCoord * 2u + uint2((traceCoord.x ^ frameIndex) & 1u, (traceCoord.y ^ (frameIndex >> 1)) & 1u), fullSize - 1u);
}

[shader("compute")]
[numthreads(8, 8, 1)]
public void csMain(uint3 id : SV_DispatchThreadID)
{
    VoxelDemosaicRoot root = gpu_load_root<VoxelDemosaicRoot>();
    uint2 traceSize = uint2(root.TraceWidth, root.TraceHeight);
    uint2 fullSize = uint2(root.FullWidth, root.FullHeight);
    if (any(id.xy >= traceSize))
        return;

    uint2 centerFull = traceCoordToFullPixel(id.xy, root.FrameIndex, root.HalfResMode, fullSize);
    int3 centerFullInt = int3(int2(centerFull), 0);

    Texture2D<float> depthTex = gpu_texture<Texture2D<float>>(root.DepthTex.index);
    Texture2D<float4> normalTex = gpu_texture<Texture2D<float4>>(root.NormalTex.index);
    Texture2D<float4> rawDiffTex = gpu_texture<Texture2D<float4>>(root.RawDiffuseTex.index);
    Texture2D<float4> rawSpecTex = gpu_texture<Texture2D<float4>>(root.RawSpecularTex.index);

    RWTexture2D<float4> outDiff = gpu_texture<RWTexture2D<float4>>(root.OutResolvedDiffuseTex.index);
    RWTexture2D<float4> outSpec = gpu_texture<RWTexture2D<float4>>(root.OutResolvedSpecularTex.index);
    RWTexture2D<float4> outHistDiff = gpu_texture<RWTexture2D<float4>>(root.OutHistoryDiffuseTex.index);
    RWTexture2D<float4> outHistSpec = gpu_texture<RWTexture2D<float4>>(root.OutHistorySpecularTex.index);

    float centerDepth = depthTex.Load(centerFullInt);
    if (centerDepth == 0.0)
    {
        float4 skyD = float4(0.0, 0.0, 0.0, 1.0);
        float4 skyS = float4(0.0, 0.0, 0.0, 0.0);
        outDiff[id.xy] = skyD;
        outSpec[id.xy] = skyS;
        outHistDiff[id.xy] = skyD;
        outHistSpec[id.xy] = skyS;
        return;
    }

    float3 centerN = normalize(normalTex.Load(centerFullInt).xyz * 2.0 - 1.0);
    float4 sumDiff = float4(0.0);
    float4 sumSpec = float4(0.0);
    float sumW = 0.0;

    float4 minDiff = float4(1e9);
    float4 maxDiff = float4(-1e9);
    float4 minSpec = float4(1e9);
    float4 maxSpec = float4(-1e9);

    int2 maxTrace = int2(traceSize) - 1;
    for (int dy = -1; dy <= 1; ++dy)
    {
        for (int dx = -1; dx <= 1; ++dx)
        {
            uint2 nCoord = uint2(clamp(int2(id.xy) + int2(dx, dy), int2(0), maxTrace));
            uint2 nFull = traceCoordToFullPixel(nCoord, root.FrameIndex, root.HalfResMode, fullSize);
            int3 nFullInt = int3(int2(nFull), 0);

            float nDepth = depthTex.Load(nFullInt);
            float4 rD = rawDiffTex.Load(int3(int2(nCoord), 0));
            float4 rS = rawSpecTex.Load(int3(int2(nCoord), 0));
            if (nDepth == 0.0)
                continue;

            float3 nNormal = normalize(normalTex.Load(nFullInt).xyz * 2.0 - 1.0);
            float relDepth = abs(centerDepth - nDepth) / max(centerDepth, 1e-4);
            float wDepth = exp(-relDepth * 40.0);
            float wNormal = pow(saturate(dot(centerN, nNormal)), 12.0);
            float wSpatial = (dx == 0 && dy == 0) ? 1.0 : 0.75;
            float w = wDepth * wNormal * wSpatial;

            sumDiff += rD * w;
            sumSpec += rS * w;
            sumW += w;

            if (w > 0.1)
            {
                minDiff = min(minDiff, rD);
                maxDiff = max(maxDiff, rD);
                minSpec = min(minSpec, rS);
                maxSpec = max(maxSpec, rS);
            }
        }
    }

    float4 spatialDiff = sumW > 1e-5 ? sumDiff / sumW : rawDiffTex.Load(int3(int2(id.xy), 0));
    float4 spatialSpec = sumW > 1e-5 ? sumSpec / sumW : rawSpecTex.Load(int3(int2(id.xy), 0));
    if (sumW <= 1e-5)
    {
        minDiff = maxDiff = spatialDiff;
        minSpec = maxSpec = spatialSpec;
    }

    float4 finalDiff = spatialDiff;
    float4 finalSpec = spatialSpec;

    if (root.HasHistory != 0u && root.TemporalBlendAlpha < 0.999)
    {
        World3DView currView = gpu_load<World3DView>(root.Views, 0u);
        World3DView prevView = gpu_load<World3DView>(root.Views, 1u);

        float2 uv = (float2(centerFull) + 0.5) * currView.ViewportSizeInvSize.zw;
        float2 ndc = uv * float2(2.0, -2.0) + float2(-1.0, 1.0);
        float4 eyeRelativeH = mul(float4(ndc, centerDepth, 1.0), currView.CameraInvVP);
        float3 eyeRelPos = eyeRelativeH.xyz / max(eyeRelativeH.w, 1e-6);

        float3 prevEyeRel = eyeRelPos
            + float3(currView.EyeCell.xyz - prevView.EyeCell.xyz) * WORLD3D_CELL_SIZE
            + (currView.EyeLocalNear.xyz - prevView.EyeLocalNear.xyz);
        float4 prevClip = mul(float4(prevEyeRel, 1.0), prevView.CameraVP);
        if (prevClip.w > 1e-5)
        {
            float2 prevUv = (prevClip.xy / prevClip.w) * float2(0.5, -0.5) + float2(0.5, 0.5);
            if (all(prevUv > 0.001 && prevUv < 0.999))
            {
                SamplerState linearClamp = shared_sampler_linear_clamp();
                float4 hDiff = gpu_texture<Texture2D<float4>>(root.PrevHistoryDiffuseTex.index).SampleLevel(linearClamp, prevUv, 0.0);
                float4 hSpec = gpu_texture<Texture2D<float4>>(root.PrevHistorySpecularTex.index).SampleLevel(linearClamp, prevUv, 0.0);

                float4 padD = max(abs(maxDiff - minDiff) * 0.35, float4(0.015));
                float4 padS = max(abs(maxSpec - minSpec) * 0.35, float4(0.015));
                hDiff = clamp(hDiff, minDiff - padD, maxDiff + padD);
                hSpec = clamp(hSpec, minSpec - padS, maxSpec + padS);

                float alpha = saturate(root.TemporalBlendAlpha);
                finalDiff = lerp(hDiff, spatialDiff, alpha);
                finalSpec = lerp(hSpec, spatialSpec, alpha);
            }
        }
    }

    outDiff[id.xy] = finalDiff;
    outSpec[id.xy] = finalSpec;
    outHistDiff[id.xy] = finalDiff;
    outHistSpec[id.xy] = finalSpec;
}
