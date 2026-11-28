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

float ssrLuma(float3 c)
{
    return dot(c, float3(0.2126, 0.7152, 0.0722));
}

float3 ssrToTonemap(float3 c)
{
    float3 clamped = max(c, float3(0.0));
    return clamped / (1.0 + ssrLuma(clamped));
}

float3 ssrFromTonemap(float3 c)
{
    return c / max(1.0 - ssrLuma(c), 1e-3);
}

void accumulateSsrTap(
    int2 traceCoord,
    int2 off,
    int2 traceDims,
    int2 fullDims,
    float nearPlane,
    float centerLinearZ,
    float3 centerNormal,
    float depthTol,
    float normExp,
    float invSigma2,
    Texture2D<float4> rawTex,
    Texture2D<float4> metaTex,
    Texture2D<float4> normalTex,
    Texture2D<float> depthTex,
    inout float confSum,
    inout float confWeightSum,
    inout float cMom1,
    inout float cMom2,
    inout float3 colorSumTm,
    inout float colorWeightSum,
    inout float3 rgbMom1,
    inout float3 rgbMom2,
    inout float hitDistSum)
{
    int2 tc = traceCoord + off;
    if (tc.x < 0 || tc.x >= traceDims.x || tc.y < 0 || tc.y >= traceDims.y)
        return;

    float2 nbUv = (float2(tc) + 0.5) / float2(traceDims);
    int2 nbFull = clamp(int2(nbUv * float2(fullDims)), int2(0, 0), fullDims - int2(1, 1));
    float nbDepth = depthTex.Load(int3(nbFull, 0));
    if (nbDepth <= 0.0)
        return;

    float nbLinearZ = nearPlane / max(nbDepth, 1e-6);
    float3 nbNormal = normalize(normalTex.Load(int3(nbFull, 0)).xyz * 2.0 - 1.0);
    float depthW = exp(-abs(nbLinearZ - centerLinearZ) / depthTol);
    float normW = pow(saturate(dot(centerNormal, nbNormal)), normExp);
    float geomW = exp(-float(off.x * off.x + off.y * off.y) * invSigma2) * depthW * normW;

    float4 s = rawTex.Load(int3(tc, 0));
    confSum += s.a * geomW;
    confWeightSum += geomW;
    cMom1 += s.a * geomW;
    cMom2 += s.a * s.a * geomW;

    if (s.a > 0.001)
    {
        float3 tm = ssrToTonemap(s.rgb);
        float cw = geomW * s.a;
        colorSumTm += tm * cw;
        colorWeightSum += cw;
        rgbMom1 += tm * cw;
        rgbMom2 += (tm * tm) * cw;
        float4 nbMeta = metaTex.Load(int3(tc, 0));
        hitDistSum += nbMeta.z * cw;
    }
}

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
    Texture2D<float4> matTex = gpu_texture<Texture2D<float4>>(root.MaterialTex.index);
    Texture2D<float> depthTex = gpu_texture<Texture2D<float>>(root.DepthTex.index);

    World3DView currView = gpu_load<World3DView>(root.Views, 0u);
    int2 traceDims = int2(int(root.TraceWidth), int(root.TraceHeight));
    int2 fullDims = max(int2(currView.ViewportSizeInvSize.xy), int2(1, 1));
    int2 maxFull = fullDims - int2(1, 1);

    float2 uv = (float2(id.xy) + 0.5) / float2(traceDims);
    int2 centerFull = clamp(int2(uv * float2(fullDims)), int2(0, 0), maxFull);
    float centerDepth = depthTex.Load(int3(centerFull, 0));
    if (centerDepth <= 0.0)
    {
        historyOut[id.xy] = float4(0.0);
        return;
    }

    float nearPlane = max(currView.EyeLocalNear.w, 1e-6);
    float centerLinearZ = nearPlane / max(centerDepth, 1e-6);
    float3 centerNormal = normalize(normalTex.Load(int3(centerFull, 0)).xyz * 2.0 - 1.0);
    float roughness = clamp(matTex.Load(int3(centerFull, 0)).g, 0.02, 1.0);

    float4 centerRaw = rawTex.Load(int3(id.xy, 0));
    float4 centerMeta = metaTex.Load(int3(id.xy, 0));

    float roughT = saturate(roughness * 1.8);
    float normExp = lerp(14.0, 2.5, roughT);
    float invSigma2 = lerp(0.55, 0.18, roughT);
    float depthTol = max(centerLinearZ * 0.06, 0.15);

    float confSum = centerRaw.a;
    float confWeightSum = 1.0;
    float cMom1 = centerRaw.a;
    float cMom2 = centerRaw.a * centerRaw.a;

    float3 colorSumTm = float3(0.0);
    float colorWeightSum = 0.0;
    float3 rgbMom1 = float3(0.0);
    float3 rgbMom2 = float3(0.0);
    float hitDistSum = 0.0;

    if (centerRaw.a > 0.001)
    {
        float3 tm0 = ssrToTonemap(centerRaw.rgb);
        float cw0 = centerRaw.a;
        colorSumTm = tm0 * cw0;
        colorWeightSum = cw0;
        rgbMom1 = tm0 * cw0;
        rgbMom2 = (tm0 * tm0) * cw0;
        hitDistSum = centerMeta.z * cw0;
    }

    static const int2 innerOffsets[8] = {
        int2(-1, -1), int2(0, -1), int2(1, -1),
        int2(-1,  0),              int2(1,  0),
        int2(-1,  1), int2(0,  1), int2(1,  1)
    };
    static const int2 outerOffsets[8] = {
        int2(-2,  0), int2(2,  0), int2(0, -2), int2(0,  2),
        int2(-2, -2), int2(2, -2), int2(-2,  2), int2(2,  2)
    };

    int2 traceCoord = int2(id.xy);
    [unroll]
    for (uint i = 0u; i < 8u; ++i)
    {
        accumulateSsrTap(
            traceCoord, innerOffsets[i], traceDims, fullDims, nearPlane, centerLinearZ, centerNormal,
            depthTol, normExp, invSigma2, rawTex, metaTex, normalTex, depthTex,
            confSum, confWeightSum, cMom1, cMom2, colorSumTm, colorWeightSum, rgbMom1, rgbMom2, hitDistSum);
    }

    if (roughness >= 0.10)
    {
        [unroll]
        for (uint j = 0u; j < 8u; ++j)
        {
            accumulateSsrTap(
                traceCoord, outerOffsets[j], traceDims, fullDims, nearPlane, centerLinearZ, centerNormal,
                depthTol, normExp, invSigma2, rawTex, metaTex, normalTex, depthTex,
                confSum, confWeightSum, cMom1, cMom2, colorSumTm, colorWeightSum, rgbMom1, rgbMom2, hitDistSum);
        }
    }

    float spatialConf = saturate(confSum / max(confWeightSum, 1e-4));
    float3 spatialTm = colorWeightSum > 1e-5 ? (colorSumTm / colorWeightSum) : float3(0.0);
    if (root.HistoryValid == 0u || root.HistoryInTex.index == 0u)
    {
        historyOut[id.xy] = float4(ssrFromTonemap(spatialTm), spatialConf);
        return;
    }

    float2 centerNdc = uv * float2(2.0, -2.0) + float2(-1.0, 1.0);
    float4 centerEyeH = mul(float4(centerNdc, centerDepth, 1.0), currView.CameraInvVP);
    float3 centerPos = centerEyeH.xyz / max(centerEyeH.w, 1e-6);

    World3DView prevView = gpu_load<World3DView>(root.Views, 1u);
    float3 cellShift = float3(currView.EyeCell.xyz - prevView.EyeCell.xyz) * WORLD3D_CELL_SIZE
        + (currView.EyeLocalNear.xyz - prevView.EyeLocalNear.xyz);

    float4 surfPrevClip = mul(float4(centerPos + cellShift, 1.0), prevView.CameraVP);
    if (surfPrevClip.w <= 1e-5)
    {
        historyOut[id.xy] = float4(ssrFromTonemap(spatialTm), spatialConf);
        return;
    }

    float2 sUv = (surfPrevClip.xy / surfPrevClip.w) * float2(0.5, -0.5) + float2(0.5, 0.5);
    float2 prevUv = sUv;
    float avgHitDist = colorWeightSum > 1e-5 ? (hitDistSum / colorWeightSum) : 0.0;
    if (avgHitDist > 0.05 && roughness < 0.30)
    {
        float3 reflDir = reflect(normalize(centerPos), centerNormal);
        float3 virtualPos = centerPos + reflDir * (avgHitDist * 0.75);
        float4 virtPrevClip = mul(float4(virtualPos + cellShift, 1.0), prevView.CameraVP);
        if (virtPrevClip.w > 1e-5)
        {
            float2 vUv = (virtPrevClip.xy / virtPrevClip.w) * float2(0.5, -0.5) + float2(0.5, 0.5);
            prevUv = lerp(vUv, sUv, saturate(roughness * 3.5));
        }
    }

    if (prevUv.x <= 0.0 || prevUv.x >= 1.0 || prevUv.y <= 0.0 || prevUv.y >= 1.0)
    {
        historyOut[id.xy] = float4(ssrFromTonemap(spatialTm), spatialConf);
        return;
    }

    Texture2D<float4> histIn = gpu_texture<Texture2D<float4>>(root.HistoryInTex.index);
    float4 prevRaw = histIn.SampleLevel(shared_sampler_linear_clamp(), prevUv, 0.0);
    float3 prevTm = ssrToTonemap(prevRaw.rgb);

    float gamma = lerp(1.5, 2.5, roughT);
    float meanConf = cMom1 / max(confWeightSum, 1e-4);
    float stdConf = sqrt(max(cMom2 / max(confWeightSum, 1e-4) - meanConf * meanConf, 0.0));
    float clampedPrevConf = clamp(prevRaw.a, meanConf - stdConf * gamma, meanConf + stdConf * gamma);

    float3 clampedPrevTm = float3(0.0);
    if (colorWeightSum > 1e-5)
    {
        float3 meanRgb = rgbMom1 / colorWeightSum;
        float3 stdRgb = sqrt(max(rgbMom2 / colorWeightSum - meanRgb * meanRgb, float3(0.0)));
        clampedPrevTm = clamp(prevTm, meanRgb - stdRgb * gamma, meanRgb + stdRgb * gamma);
    }

    float uvDeltaPx = length((prevUv - uv) * float2(traceDims));
    float motionFactor = lerp(1.0, 0.72, saturate(uvDeltaPx * 0.08));
    float confJumpFactor = lerp(1.0, 0.65, saturate(abs(prevRaw.a - spatialConf) * 2.5));
    float blend = saturate(root.TemporalBlend * motionFactor * confJumpFactor);

    float3 finalTm = colorWeightSum > 1e-5
        ? lerp(spatialTm, clampedPrevTm, blend)
        : lerp(float3(0.0), clampedPrevTm, blend * 0.5);
    float finalConf = lerp(spatialConf, clampedPrevConf, blend);
    historyOut[id.xy] = float4(ssrFromTonemap(finalTm), saturate(finalConf));
}
