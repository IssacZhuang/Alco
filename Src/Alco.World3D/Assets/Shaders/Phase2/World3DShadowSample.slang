#language slang 2025
module World3DShadowSample;

import Core;
import SharedSampler;
import World3DSceneData;

public static const uint WORLD3D_SHADOW_FLAG_ENABLED = 1u;
public static const uint WORLD3D_SHADOW_FLAG_USE_MINMAX = 2u;
public static const uint WORLD3D_SHADOW_FLAG_STATIC_FAR_ACTIVE = 4u;
public static const uint WORLD3D_SHADOW_FLAG_FAR_HEIGHT_ACTIVE = 8u;
public static const uint WORLD3D_SHADOW_FLAG_RSM_ACTIVE = 16u;
public static const uint WORLD3D_SHADOW_FLAG_HIZ_ACTIVE = 32u;

public struct World3DCascadeGpu
{
    public row_major float4x4 LightViewProj;
    public int4 CascadeCell;
    public float4 CascadeLocal; // xyz = anchor local in cell, w = world extent (meters)
};

public struct World3DShadowFrameGpu
{
    public World3DCascadeGpu Cascade0;
    public World3DCascadeGpu Cascade1;
    public World3DCascadeGpu Cascade2;
    public World3DCascadeGpu Cascade3;
    public World3DCascadeGpu Cascade4;
    public float4 CascadeSplits;          // view/distance outer split per main cascade
    public float4 FarCascadeParams;       // x = staticFarSplit, y = farTransitionWidth, z = maxDistance, w = shadowTightness
    public float4 ResolutionParams;       // x = cascadeRes, y = invCascadeRes, z = minMaxRes, w = normalBiasScale
    public float4 FarHeightParams;        // x = invCoverageMeters, y = halfCoverageMeters, z = horizonMaxDist, w = stepMeters
    public float4 SunDirectionParams;     // xyz = sunDirToLight, w = reserved
    public float4 RsmParams;              // x = rsmExtent, y = invRsmRes, z = reserved0, w = reserved1
    public uint Flags;
    public uint HiZMip0Width;
    public uint HiZMip0Height;
    public uint HiZMipCount;
    public uint DebugView;
    public uint RsmResolution;
    public uint FarHeightResolution;
    public uint FarHeightMipCount;
    public GPUTextureId ShadowArray;
    public GPUTextureId ShadowMinMaxArray;
    public GPUTextureId FarHeightTex;
    public GPUTextureId HiZTex;
    public GPUTextureId RsmFluxTex;
    public GPUTextureId RsmNormalTex;
    public uint Reserved0;
    public uint Reserved1;
};

public struct World3DShadowEval
{
    public float Visibility;
    public int CascadeIndex;      // 0..3 main, 4 static far, -1 outside cascades
    public uint MinMaxState;      // 0 = disabled/untested, 1 = full lit, 2 = full shadow, 3 = mixed PCF
    public float FarBlendWeight;
};

[ForceInline]
World3DCascadeGpu getCascade(World3DShadowFrameGpu frame, uint cascadeIdx)
{
    if (cascadeIdx == 0u) return frame.Cascade0;
    if (cascadeIdx == 1u) return frame.Cascade1;
    if (cascadeIdx == 2u) return frame.Cascade2;
    if (cascadeIdx == 3u) return frame.Cascade3;
    return frame.Cascade4;
}

float3 projectToCascade(World3DShadowFrameGpu frame, uint cascadeIdx, World3DView view, float3 eyeRelPos, float3 normalWS)
{
    World3DCascadeGpu c = getCascade(frame, cascadeIdx);
    float texelWorld = c.CascadeLocal.w * frame.ResolutionParams.y;
    float ndotL = saturate(dot(normalWS, frame.SunDirectionParams.xyz));
    float normalBiasWeight = lerp(1.6, 0.5, saturate(frame.FarCascadeParams.w)) * saturate(1.0 - ndotL);
    float3 biasedEyeRel = eyeRelPos + normalWS * (texelWorld * frame.ResolutionParams.w * normalBiasWeight);

    float3 relToCascade = float3(view.EyeCell.xyz - c.CascadeCell.xyz) * WORLD3D_CELL_SIZE
        + (view.EyeLocalNear.xyz - c.CascadeLocal.xyz)
        + biasedEyeRel;

    float4 clip = mul(float4(relToCascade, 1.0), c.LightViewProj);
    float invW = rcp(max(abs(clip.w), 1e-6));
    float2 ndc = clip.xy * invW;
    float2 uv = ndc * float2(0.5, -0.5) + float2(0.5, 0.5);
    return float3(uv, clip.z * invW);
}

bool insideCascade(float3 uvz, float margin)
{
    return uvz.x >= margin && uvz.x <= 1.0 - margin
        && uvz.y >= margin && uvz.y <= 1.0 - margin
        && uvz.z > 0.0 && uvz.z < 1.0;
}

float computeReceiverBias(World3DShadowFrameGpu frame, uint cascadeIdx, float ndotL)
{
    World3DCascadeGpu c = getCascade(frame, cascadeIdx);
    float texelWorld = c.CascadeLocal.w * frame.ResolutionParams.y;
    float depthPerMeter = abs(mul(float4(-frame.SunDirectionParams.xyz, 0.0), c.LightViewProj).z);
    float slope = sqrt(max(1.0 - ndotL * ndotL, 0.0)) / max(ndotL, 0.15);
    float tightnessScale = lerp(1.15, 0.85, saturate(frame.FarCascadeParams.w));
    return texelWorld * depthPerMeter * (0.65 + 0.9 * slope) * tightnessScale;
}

float evaluateCascadePcf(World3DShadowFrameGpu frame, uint cascadeIdx, float3 uvz, float bias, out uint minMaxState)
{
    minMaxState = 0u;
    float cmpDepth = uvz.z - bias;
    if (cmpDepth <= 0.0)
        return 1.0;

    if ((frame.Flags & WORLD3D_SHADOW_FLAG_USE_MINMAX) != 0u && frame.ShadowMinMaxArray.index != 0u)
    {
        Texture2DArray<float2> minMaxTex = gpu_texture<Texture2DArray<float2>>(frame.ShadowMinMaxArray.index);
        int maxMm = int(max(frame.ResolutionParams.z, 1.0)) - 1;
        int2 mmCoord = clamp(int2(floor(uvz.xy * frame.ResolutionParams.z)), int2(0), int2(maxMm));
        float2 mm = minMaxTex.Load(int4(mmCoord.x, mmCoord.y, int(cascadeIdx), 0));
        if (cmpDepth < mm.x - 1e-5)
        {
            minMaxState = 1u;
            return 1.0;
        }
        if (cmpDepth > mm.y + 1e-5)
        {
            minMaxState = 2u;
            return 0.0;
        }
        minMaxState = 3u;
    }

    Texture2DArray<float> shadowTex = gpu_texture<Texture2DArray<float>>(frame.ShadowArray.index);
    SamplerComparisonState cmpSampler = shared_sampler_depth_comparison();
    float texel = frame.ResolutionParams.y;
    float tightness = saturate(frame.FarCascadeParams.w);
    float spread = lerp(1.0, 0.35, tightness);
    float layer = float(cascadeIdx);

    if (cascadeIdx == 0u)
    {
        // Separable 4x4 tent-weighted hardware 2x2 bilinear taps spanning up to +-3 texels (7x7 equivalent).
        const float offs[4] = { -2.4, -0.8, 0.8, 2.4 };
        const float weights[4] = { 0.16, 0.34, 0.34, 0.16 };
        float sum = 0.0;
        [unroll]
        for (uint iy = 0u; iy < 4u; ++iy)
        {
            [unroll]
            for (uint ix = 0u; ix < 4u; ++ix)
            {
                float2 o = float2(offs[ix], offs[iy]) * (texel * spread);
                sum += shadowTex.SampleCmpLevelZero(cmpSampler, float3(uvz.xy + o, layer), cmpDepth)
                    * (weights[ix] * weights[iy]);
            }
        }
        return saturate(sum);
    }

    // Separable 3x3 Gaussian-weighted hardware 2x2 bilinear taps (5x5 equivalent).
    const float offs3[3] = { -1.65, 0.0, 1.65 };
    const float weights3[3] = { 0.25, 0.50, 0.25 };
    float sum = 0.0;
    [unroll]
    for (uint iy = 0u; iy < 3u; ++iy)
    {
        [unroll]
        for (uint ix = 0u; ix < 3u; ++ix)
        {
            float2 o = float2(offs3[ix], offs3[iy]) * (texel * spread);
            sum += shadowTex.SampleCmpLevelZero(cmpSampler, float3(uvz.xy + o, layer), cmpDepth)
                * (weights3[ix] * weights3[iy]);
        }
    }
    return saturate(sum);
}

public float world3d_sample_far_horizon_shadow(World3DShadowFrameGpu frame, World3DView view, float3 eyeRelPos)
{
    if ((frame.Flags & WORLD3D_SHADOW_FLAG_FAR_HEIGHT_ACTIVE) == 0u || frame.FarHeightTex.index == 0u)
        return 1.0;

    float3 sunDir = frame.SunDirectionParams.xyz;
    if (sunDir.z <= 0.002)
        return 0.0;

    float horizLen = length(sunDir.xy);
    if (horizLen <= 1e-4)
        return 1.0;

    float2 rayXY = sunDir.xy / horizLen;
    float dzPerMeter = sunDir.z / horizLen;

    float3 anchorRel = float3(view.EyeCell.xyz - view.OriginAnchorCell.xyz) * WORLD3D_CELL_SIZE
        + (view.EyeLocalNear.xyz + eyeRelPos);

    Texture2D<float2> heightTex = gpu_texture<Texture2D<float2>>(frame.FarHeightTex.index);
    SamplerState nearestClamp = shared_sampler_nearest_clamp();

    float invCov = frame.FarHeightParams.x;
    float maxDist = frame.FarHeightParams.z;
    float baseStep = max(frame.FarHeightParams.w, 8.0);
    uint maxMip = frame.FarHeightMipCount > 0u ? frame.FarHeightMipCount - 1u : 0u;

    float maxOcc = 0.0;
    float dist = baseStep * 1.5;

    [unroll]
    for (uint step = 0u; step < 12u; ++step)
    {
        if (dist > maxDist || maxOcc >= 0.995)
            break;

        float2 sampleUV = (anchorRel.xy + rayXY * dist) * invCov + float2(0.5, 0.5);
        if (sampleUV.x < 0.0 || sampleUV.x > 1.0 || sampleUV.y < 0.0 || sampleUV.y > 1.0)
            break;

        float mipLevel = float(min(step / 3u, min(maxMip, 3u)));
        float maxH = heightTex.SampleLevel(nearestClamp, sampleUV, mipLevel).r;
        float rayZ = anchorRel.z + dzPerMeter * dist;

        if (maxH > -99999.0 && maxH > rayZ)
        {
            if (mipLevel > 0.5)
            {
                float fineH = heightTex.SampleLevel(nearestClamp, sampleUV, 0.0).r;
                if (fineH > -99999.0 && fineH > rayZ)
                {
                    float margin = fineH - rayZ - max(2.0, dist * 0.0025);
                    if (margin > 0.0)
                    {
                        float distFade = 1.0 - smoothstep(maxDist * 0.75, maxDist, dist);
                        maxOcc = max(maxOcc, saturate(margin / max(4.0, dist * 0.02)) * distFade);
                    }
                }
            }
            else
            {
                float margin = maxH - rayZ - max(2.0, dist * 0.0025);
                if (margin > 0.0)
                {
                    float distFade = 1.0 - smoothstep(maxDist * 0.75, maxDist, dist);
                    maxOcc = max(maxOcc, saturate(margin / max(4.0, dist * 0.02)) * distFade);
                }
            }
        }
        dist *= 1.55;
    }

    return 1.0 - saturate(maxOcc);
}

public World3DShadowEval world3d_evaluate_sun_shadow(
    World3DShadowFrameGpu frame,
    World3DView view,
    float3 eyeRelPos,
    float3 normalWS)
{
    World3DShadowEval eval;
    eval.Visibility = 1.0;
    eval.CascadeIndex = -1;
    eval.MinMaxState = 0u;
    eval.FarBlendWeight = 0.0;

    if ((frame.Flags & WORLD3D_SHADOW_FLAG_ENABLED) == 0u)
        return eval;

    float ndotL = dot(normalWS, frame.SunDirectionParams.xyz);
    if (ndotL <= 0.0)
    {
        eval.Visibility = 0.0;
        return eval;
    }

    float viewDist = length(eyeRelPos);
    bool hasMain = frame.ShadowArray.index != 0u;
    float cascadeVis = 1.0;
    bool foundCascade = false;

    if (hasMain)
    {
        [unroll]
        for (uint c = 0u; c < 4u; ++c)
        {
            float3 uvz = projectToCascade(frame, c, view, eyeRelPos, normalWS);
            if (!insideCascade(uvz, 0.002))
                continue;

            float bias = computeReceiverBias(frame, c, ndotL);
            cascadeVis = evaluateCascadePcf(frame, c, uvz, bias, eval.MinMaxState);
            eval.CascadeIndex = int(c);
            foundCascade = true;

            float edgeDist = max(abs(uvz.x - 0.5), abs(uvz.y - 0.5)) * 2.0;
            float splitWeight = smoothstep(0.75, 0.98, edgeDist);
            if (splitWeight > 0.001)
            {
                if (c < 3u)
                {
                    float3 nextUvz = projectToCascade(frame, c + 1u, view, eyeRelPos, normalWS);
                    if (insideCascade(nextUvz, 0.002))
                    {
                        uint nextState;
                        float nextBias = computeReceiverBias(frame, c + 1u, ndotL);
                        float nextVis = evaluateCascadePcf(frame, c + 1u, nextUvz, nextBias, nextState);
                        cascadeVis = lerp(cascadeVis, nextVis, splitWeight);
                    }
                }
                else if ((frame.Flags & WORLD3D_SHADOW_FLAG_STATIC_FAR_ACTIVE) != 0u)
                {
                    float3 farUvz = projectToCascade(frame, 4u, view, eyeRelPos, normalWS);
                    if (insideCascade(farUvz, 0.002))
                    {
                        uint farState;
                        float farBias = computeReceiverBias(frame, 4u, ndotL);
                        float farVis = evaluateCascadePcf(frame, 4u, farUvz, farBias, farState);
                        cascadeVis = lerp(cascadeVis, farVis, splitWeight);
                    }
                }
            }
            break;
        }

        if (!foundCascade && (frame.Flags & WORLD3D_SHADOW_FLAG_STATIC_FAR_ACTIVE) != 0u)
        {
            float3 farUvz = projectToCascade(frame, 4u, view, eyeRelPos, normalWS);
            if (insideCascade(farUvz, 0.002))
            {
                float farBias = computeReceiverBias(frame, 4u, ndotL);
                cascadeVis = evaluateCascadePcf(frame, 4u, farUvz, farBias, eval.MinMaxState);
                eval.CascadeIndex = 4;
                foundCascade = true;
            }
        }
    }

    float farHorizon = world3d_sample_far_horizon_shadow(frame, view, eyeRelPos);
    bool hasFarHeight = (frame.Flags & WORLD3D_SHADOW_FLAG_FAR_HEIGHT_ACTIVE) != 0u;

    if (!foundCascade)
    {
        eval.FarBlendWeight = hasFarHeight ? 1.0 : 0.0;
        eval.Visibility = farHorizon;
        return eval;
    }

    float outerEnd = (frame.Flags & WORLD3D_SHADOW_FLAG_STATIC_FAR_ACTIVE) != 0u
        ? frame.FarCascadeParams.x
        : frame.FarCascadeParams.z;
    float transitionStart = outerEnd * 0.72;
    float outerFade = smoothstep(transitionStart, outerEnd, viewDist);
    eval.FarBlendWeight = hasFarHeight ? outerFade : 0.0;

    if (hasFarHeight)
    {
        float combined = min(cascadeVis, farHorizon);
        eval.Visibility = lerp(cascadeVis, lerp(combined, farHorizon, outerFade), saturate(outerFade * 1.25));
    }
    else
    {
        eval.Visibility = lerp(cascadeVis, 1.0, outerFade);
    }

    return eval;
}
