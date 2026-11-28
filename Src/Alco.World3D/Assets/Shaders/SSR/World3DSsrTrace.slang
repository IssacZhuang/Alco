#language slang 2025
module World3DSsrTrace;

import Core;
import SharedSampler;
import World3DSceneData;
import World3DAtmosphere;

[GPURoot]
public struct SsrTraceRoot
{
    public float4 SsrParams;            // x = maxDistance, y = thickness, z = maxRoughness, w = edgeFade
    public float4 ScreenAndStepParams;  // x = traceWidth, y = traceHeight, z = hizMipCount, w = maxSteps
    public GPUBufferView Views;
    public GPUTextureId HdrInTex;
    public GPUTextureId ColorPyramidTex;
    public GPUTextureId NormalTex;
    public GPUTextureId MaterialTex;
    public GPUTextureId DepthTex;
    public GPUTextureId HiZTex;
    public GPUTextureId BlueNoiseTex;
    public GPUTextureId OutRawColorTex;
    public GPUTextureId OutMetaTex;
    public uint FrameIndex;
    public uint HiZActive;
    public uint RejectBackface;
    public uint ColorPyramidMipCount;
    public uint Reserved0;
};

float ssrLinearViewZ(float nearPlane, float depth)
{
    return nearPlane / max(depth, 1e-6);
}

[shader("compute")]
[numthreads(8, 8, 1)]
public void csMain(uint3 id : SV_DispatchThreadID)
{
    SsrTraceRoot root = gpu_load_root<SsrTraceRoot>();
    uint traceW = uint(root.ScreenAndStepParams.x);
    uint traceH = uint(root.ScreenAndStepParams.y);
    if (id.x >= traceW || id.y >= traceH)
        return;

    RWTexture2D<float4> outColor = gpu_texture<RWTexture2D<float4>>(root.OutRawColorTex.index);
    RWTexture2D<float4> outMeta = gpu_texture<RWTexture2D<float4>>(root.OutMetaTex.index);

    World3DView view = gpu_load<World3DView>(root.Views, 0u);
    uint2 fullDims = max(uint2(view.ViewportSizeInvSize.xy), uint2(1u));
    int2 maxFull = int2(fullDims) - int2(1, 1);

    float2 uv = (float2(id.xy) + 0.5) / float2(float(traceW), float(traceH));
    int2 fullCoord = clamp(int2(uv * float2(fullDims)), int2(0, 0), maxFull);

    Texture2D<float> depthTex = gpu_texture<Texture2D<float>>(root.DepthTex.index);
    float depth = depthTex.Load(int3(fullCoord, 0));
    if (depth <= 0.0)
    {
        outColor[id.xy] = float4(0.0);
        outMeta[id.xy] = float4(0.0);
        return;
    }

    Texture2D<float4> matTex = gpu_texture<Texture2D<float4>>(root.MaterialTex.index);
    float3 mat = matTex.Load(int3(fullCoord, 0)).rgb;
    float roughness = clamp(mat.y, 0.02, 1.0);
    float maxRoughness = root.SsrParams.z;
    if (roughness >= maxRoughness)
    {
        outColor[id.xy] = float4(0.0);
        outMeta[id.xy] = float4(uv, 0.0, roughness);
        return;
    }

    float2 ndc = uv * float2(2.0, -2.0) + float2(-1.0, 1.0);
    float4 eyeH = mul(float4(ndc, depth, 1.0), view.CameraInvVP);
    float3 eyeRelPos = eyeH.xyz / max(eyeH.w, 1e-6);
    float viewDist = length(eyeRelPos);
    float3 v = -eyeRelPos / max(viewDist, 1e-6);

    Texture2D<float4> normalTex = gpu_texture<Texture2D<float4>>(root.NormalTex.index);
    float3 n = normalize(normalTex.Load(int3(fullCoord, 0)).xyz * 2.0 - 1.0);
    float ndotV = dot(n, v);
    if (ndotV < -0.15)
    {
        outColor[id.xy] = float4(0.0);
        outMeta[id.xy] = float4(uv, 0.0, roughness);
        return;
    }
    if (ndotV < 0.02)
        n = normalize(n + v * (0.02 - ndotV));

    float3 reflDir = reflect(-v, n);
    float facingWeight = 1.0 - smoothstep(0.25, 0.75, dot(reflDir, v));
    if (facingWeight <= 0.001)
    {
        outColor[id.xy] = float4(0.0);
        outMeta[id.xy] = float4(uv, 0.0, roughness);
        return;
    }

    float maxDist = max(root.SsrParams.x, 1.0);
    float nearPlane = max(view.EyeLocalNear.w, 0.05);
    float originBias = clamp(viewDist * 0.005, 0.02, 0.15);
    float3 rayOrigin = eyeRelPos + n * originBias;
    float4 c0 = mul(float4(rayOrigin, 1.0), view.CameraVP);
    float4 c1 = mul(float4(rayOrigin + reflDir * maxDist, 1.0), view.CameraVP);

    // Clip ray segment against W >= nearPlane in clip space so projection never flips behind camera.
    if (c1.w < nearPlane)
    {
        float clipT = saturate((nearPlane - c0.w) / (c1.w - c0.w));
        c1 = lerp(c0, c1, clipT);
    }

    float2 startUv = (c0.xy / max(c0.w, 1e-5)) * float2(0.5, -0.5) + float2(0.5, 0.5);
    float startZ = saturate(c0.z / max(c0.w, 1e-5));
    float2 endUv = (c1.xy / max(c1.w, 1e-5)) * float2(0.5, -0.5) + float2(0.5, 0.5);
    float endZ = saturate(c1.z / max(c1.w, 1e-5));

    // Strictly clip projected screen ray to [0, 1]^2 so Hi-Z DDA never marches off-screen.
    float2 rawDeltaUv = endUv - startUv;
    float rawDeltaZ = endZ - startZ;
    float tMin = 0.0;
    float tMax = 1.0;
    if (abs(rawDeltaUv.x) > 1e-6)
    {
        float tx0 = (0.0 - startUv.x) / rawDeltaUv.x;
        float tx1 = (1.0 - startUv.x) / rawDeltaUv.x;
        tMin = max(tMin, min(tx0, tx1));
        tMax = min(tMax, max(tx0, tx1));
    }
    else if (startUv.x < 0.0 || startUv.x > 1.0)
    {
        tMax = -1.0;
    }
    if (abs(rawDeltaUv.y) > 1e-6)
    {
        float ty0 = (0.0 - startUv.y) / rawDeltaUv.y;
        float ty1 = (1.0 - startUv.y) / rawDeltaUv.y;
        tMin = max(tMin, min(ty0, ty1));
        tMax = min(tMax, max(ty0, ty1));
    }
    else if (startUv.y < 0.0 || startUv.y > 1.0)
    {
        tMax = -1.0;
    }

    if (tMax <= tMin)
    {
        outColor[id.xy] = float4(0.0);
        outMeta[id.xy] = float4(uv, 0.0, roughness);
        return;
    }

    float3 rayStart = float3(startUv, startZ) + float3(rawDeltaUv, rawDeltaZ) * tMin;
    float3 rayDelta = float3(rawDeltaUv, rawDeltaZ) * (tMax - tMin);
    float2 pStart = rayStart.xy * float2(fullDims);
    float2 pDelta = rayDelta.xy * float2(fullDims);
    float pixelLen = length(pDelta);
    if (pixelLen < 1.5)
    {
        outColor[id.xy] = float4(0.0);
        outMeta[id.xy] = float4(uv, 0.0, roughness);
        return;
    }

    // Static spatial-only blue noise eliminates frame-to-frame shimmer on stationary cameras.
    Texture2D<float4> blueNoiseTex = gpu_texture<Texture2D<float4>>(root.BlueNoiseTex.index);
    float4 bn = blueNoiseTex.Load(int3(id.xy & 127u, 0));

    uint maxSteps = clamp(uint(root.ScreenAndStepParams.w), 8u, 128u);
    float curT = min((1.5 + bn.x) / max(pixelLen, 1.0), 0.04);
    float prevT = curT;
    bool useHiZ = root.HiZActive != 0u && root.HiZTex.index != 0u;
    int hizMipCount = int(root.ScreenAndStepParams.z);

    bool hitFound = false;
    float hitT = 0.0;

    if (useHiZ && hizMipCount > 0)
    {
        Texture2D<float2> hizTex = gpu_texture<Texture2D<float2>>(root.HiZTex.index);
        // Level 0 = full-res 1x1 depthTex, level k >= 1 = hizTex mip (k - 1).
        int maxLevel = min(hizMipCount, 6);
        int level = min(2, maxLevel);
        uint2 mip0Dims = max(uint2(1u), fullDims >> 1u);
        float pxEps = 0.05 / max(pixelLen, 1.0);

        for (uint iter = 0u; iter < maxSteps && curT < 1.0; ++iter)
        {
            float2 curPx = pStart + pDelta * curT;
            if (curPx.x < 0.0 || curPx.x >= float(fullDims.x) || curPx.y < 0.0 || curPx.y >= float(fullDims.y))
                break;

            int2 levelDims = level == 0 ? int2(fullDims) : max(int2(1, 1), int2(mip0Dims >> uint(level - 1)));
            float cellSize = float(1u << uint(level));
            int2 cell = clamp(int2(floor(curPx / cellSize)), int2(0, 0), levelDims - int2(1, 1));

            float nextEdgeX = pDelta.x >= 0.0
                ? (cell.x == levelDims.x - 1 ? float(fullDims.x) : float(cell.x + 1) * cellSize)
                : float(cell.x) * cellSize;
            float nextEdgeY = pDelta.y >= 0.0
                ? (cell.y == levelDims.y - 1 ? float(fullDims.y) : float(cell.y + 1) * cellSize)
                : float(cell.y) * cellSize;

            float tx = abs(pDelta.x) > 1e-4 ? (nextEdgeX - curPx.x) / pDelta.x : 1.0e9;
            float ty = abs(pDelta.y) > 1e-4 ? (nextEdgeY - curPx.y) / pDelta.y : 1.0e9;
            float stepT = max(min(tx, ty), 0.0) + pxEps;
            float nextT = min(curT + stepT, 1.0);

            float curZ = rayStart.z + rayDelta.z * curT;
            float nextZ = rayStart.z + rayDelta.z * nextT;
            float segMinZ = min(curZ, nextZ);
            float segMaxZ = max(curZ, nextZ);

            if (level > 0)
            {
                float2 cellZ = hizTex.Load(int3(cell, level - 1));
                if (segMinZ > cellZ.y + 1e-6)
                {
                    prevT = curT;
                    curT = nextT;
                    level = min(level + 1, maxLevel);
                    continue;
                }

                float cellThickness = root.SsrParams.y * (1.0 + nextT * 0.5);
                if (cellZ.x > 0.0 && segMaxZ < cellZ.x)
                {
                    float segNearLinZ = ssrLinearViewZ(nearPlane, segMaxZ);
                    float cellFarLinZ = ssrLinearViewZ(nearPlane, cellZ.x);
                    if ((segNearLinZ - cellFarLinZ) > cellThickness * 1.5)
                    {
                        prevT = curT;
                        curT = nextT;
                        level = min(level + 1, maxLevel);
                        continue;
                    }
                }

                level -= 1;
                continue;
            }

            float sampleT = 0.5 * (curT + nextT);
            float rayZ = rayStart.z + rayDelta.z * sampleT;
            float sceneDepth = depthTex.Load(int3(cell, 0));
            if (sceneDepth > 0.0 && rayZ <= sceneDepth)
            {
                float rayLinZ = ssrLinearViewZ(nearPlane, rayZ);
                float sceneLinZ = ssrLinearViewZ(nearPlane, sceneDepth);
                float zSpan = abs(ssrLinearViewZ(nearPlane, nextZ) - ssrLinearViewZ(nearPlane, curZ));
                float thickness = max(root.SsrParams.y * (1.0 + sampleT * 0.5), min(zSpan * 1.5, root.SsrParams.y * 2.5));
                if (rayLinZ - sceneLinZ <= thickness)
                {
                    hitFound = true;
                    prevT = curT;
                    hitT = sampleT;
                    break;
                }
            }

            prevT = curT;
            curT = nextT;
            level = 1;
        }
    }
    else
    {
        float dt = 1.0 / float(maxSteps);
        float t = curT;
        for (uint iter = 0u; iter < maxSteps && t <= 1.0; ++iter)
        {
            float2 sampleUv = rayStart.xy + rayDelta.xy * t;
            int2 px = clamp(int2(sampleUv * float2(fullDims)), int2(0, 0), maxFull);
            float sceneDepth = depthTex.Load(int3(px, 0));
            float rayZ = rayStart.z + rayDelta.z * t;
            if (sceneDepth > 0.0 && rayZ <= sceneDepth)
            {
                float rayLinZ = ssrLinearViewZ(nearPlane, rayZ);
                float sceneLinZ = ssrLinearViewZ(nearPlane, sceneDepth);
                float prevZ = rayStart.z + rayDelta.z * prevT;
                float zSpan = abs(rayLinZ - ssrLinearViewZ(nearPlane, prevZ));
                float thickness = max(root.SsrParams.y * (1.0 + t * 0.5), min(zSpan * 1.5, root.SsrParams.y * 2.5));
                if (rayLinZ - sceneLinZ <= thickness)
                {
                    hitFound = true;
                    hitT = t;
                    break;
                }
            }
            prevT = t;
            t = min(t + dt, 1.0 + 1e-4);
        }
    }

    if (!hitFound)
    {
        outColor[id.xy] = float4(0.0);
        outMeta[id.xy] = float4(uv, 0.0, roughness);
        return;
    }

    // 4-step binary refinement between prevT and hitT.
    float lo = prevT;
    float hi = hitT;
    [unroll]
    for (uint r = 0u; r < 4u; ++r)
    {
        float midT = 0.5 * (lo + hi);
        float2 midUv = rayStart.xy + rayDelta.xy * midT;
        int2 px = clamp(int2(midUv * float2(fullDims)), int2(0, 0), maxFull);
        float sd = depthTex.Load(int3(px, 0));
        float midRayZ = rayStart.z + rayDelta.z * midT;
        if (sd > 0.0 && midRayZ <= sd)
            hi = midT;
        else
            lo = midT;
    }

    float2 hitUv = rayStart.xy + rayDelta.xy * hi;
    if (hitUv.x <= 0.0 || hitUv.x >= 1.0 || hitUv.y <= 0.0 || hitUv.y >= 1.0)
    {
        outColor[id.xy] = float4(0.0);
        outMeta[id.xy] = float4(uv, 0.0, roughness);
        return;
    }

    int2 hitPx = clamp(int2(hitUv * float2(fullDims)), int2(0, 0), maxFull);
    float finalSceneDepth = depthTex.Load(int3(hitPx, 0));
    float finalRayZ = rayStart.z + rayDelta.z * hi;
    float finalRayLinZ = ssrLinearViewZ(nearPlane, finalRayZ);
    float finalSceneLinZ = finalSceneDepth > 0.0 ? ssrLinearViewZ(nearPlane, finalSceneDepth) : 1.0e8;
    float finalThickness = root.SsrParams.y * (1.0 + hi * 0.5);
    if (finalSceneDepth <= 0.0 || abs(finalRayLinZ - finalSceneLinZ) > finalThickness)
    {
        outColor[id.xy] = float4(0.0);
        outMeta[id.xy] = float4(uv, 0.0, roughness);
        return;
    }

    float2 hitNdc = hitUv * float2(2.0, -2.0) + float2(-1.0, 1.0);
    float4 hitEyeH = mul(float4(hitNdc, finalSceneDepth, 1.0), view.CameraInvVP);
    float3 hitEyeRel = hitEyeH.xyz / max(hitEyeH.w, 1e-6);
    float hitWorldDist = length(hitEyeRel - eyeRelPos);
    if (hitWorldDist < originBias * 1.25 || hitWorldDist > maxDist * 1.05)
    {
        outColor[id.xy] = float4(0.0);
        outMeta[id.xy] = float4(uv, 0.0, roughness);
        return;
    }

    if (root.RejectBackface != 0u)
    {
        float3 hitNormal = normalize(normalTex.Load(int3(hitPx, 0)).xyz * 2.0 - 1.0);
        if (dot(reflDir, hitNormal) > 0.15)
        {
            outColor[id.xy] = float4(0.0);
            outMeta[id.xy] = float4(uv, 0.0, roughness);
            return;
        }
    }

    Texture2D<float4> hdrIn = gpu_texture<Texture2D<float4>>(root.HdrInTex.index);
    float3 cCenter = hdrIn.SampleLevel(shared_sampler_linear_clamp(), hitUv, 0.0).rgb;

    float projScaleY = length(float3(view.CameraVP[0].y, view.CameraVP[1].y, view.CameraVP[2].y));
    float coneTanHalf = roughness * roughness * 1.25;
    float coneRadiusPx = hitWorldDist * coneTanHalf * (0.5 * float(fullDims.y) * projScaleY) / max(finalSceneLinZ, 0.25);
    float minRoughLod = roughness * roughness * 3.5;
    float targetLod = max(log2(max(coneRadiusPx, 1.0)), minRoughLod);
    float maxTotalLod = float(root.ColorPyramidMipCount);
    float clampedLod = clamp(targetLod, 0.0, maxTotalLod);

    float3 hitColor = cCenter;
    if (root.ColorPyramidMipCount > 0u && root.ColorPyramidTex.index != 0u && clampedLod > 0.01)
    {
        Texture2D<float4> colorPyr = gpu_texture<Texture2D<float4>>(root.ColorPyramidTex.index);
        SamplerState clampSampler = shared_sampler_linear_clamp();
        if (clampedLod < 1.0)
        {
            float3 mip0Color = colorPyr.SampleLevel(clampSampler, hitUv, 0.0).rgb;
            hitColor = lerp(cCenter, mip0Color, clampedLod);
        }
        else
        {
            float pyrLod = min(clampedLod - 1.0, max(maxTotalLod - 1.0, 0.0));
            float3 pyrCenter = colorPyr.SampleLevel(clampSampler, hitUv, pyrLod).rgb;
            hitColor = pyrCenter;

            float overflow = targetLod - maxTotalLod;
            if (overflow > 0.15)
            {
                static const float2 diskOffsets[4] = {
                    float2( 0.85,  0.25),
                    float2(-0.25,  0.85),
                    float2(-0.85, -0.25),
                    float2( 0.25, -0.85)
                };
                float2 mipTexelUv = float2(float(1u << min(root.ColorPyramidMipCount, 12u))) * view.ViewportSizeInvSize.zw;
                float2 tapRadiusUv = mipTexelUv * clamp(exp2(min(overflow, 2.0)) - 1.0, 0.0, 2.5);
                float3 accum = pyrCenter;
                [unroll]
                for (uint ti = 0u; ti < 4u; ++ti)
                {
                    float2 rotOff = float2(
                        diskOffsets[ti].x * bn.z - diskOffsets[ti].y * bn.w,
                        diskOffsets[ti].x * bn.w + diskOffsets[ti].y * bn.z);
                    float2 tapUv = clamp(hitUv + rotOff * tapRadiusUv, float2(0.002), float2(0.998));
                    accum += colorPyr.SampleLevel(clampSampler, tapUv, pyrLod).rgb;
                }
                hitColor = accum * 0.2;
            }
        }
    }

    float edgeFade = max(root.SsrParams.w, 0.01);
    float edgeDist = min(min(hitUv.x, 1.0 - hitUv.x), min(hitUv.y, 1.0 - hitUv.y));
    float borderWeight = smoothstep(0.0, edgeFade, edgeDist);
    float distWeight = 1.0 - smoothstep(0.75, 1.0, saturate(hitWorldDist / maxDist));
    float confidence = saturate(borderWeight * distWeight * facingWeight);

    outColor[id.xy] = float4(hitColor, confidence);
    outMeta[id.xy] = float4(hitUv, max(hitWorldDist, 0.01), roughness);
}
