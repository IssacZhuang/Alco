#language slang 2025
module World3DVoxelTrace;

import Core;
import SharedSampler;
import World3DSceneData;
import World3DShadowSample;
import World3DVoxelCommon;

[GPURoot]
public struct VoxelTraceRoot
{
    public GPUBufferView Frame;
    public GPUBufferView Views;
    public GPUBufferView ShadowFrame;
    public GPUTextureId DepthTex;
    public GPUTextureId NormalTex;
    public GPUTextureId EmissiveTex;
    public GPUTextureId MaterialTex;
    public GPUTextureId Vol0;
    public GPUTextureId Vol1;
    public GPUTextureId Vol2;
    public GPUTextureId Vol3;
    public GPUTextureId OutDiffuseTex;
    public GPUTextureId OutSpecularTex;
};

float3 decodeGeometryNormal(float2 encoded)
{
    float2 f = encoded * 2.0 - 1.0;
    float3 gn = float3(f.x, f.y, 1.0 - abs(f.x) - abs(f.y));
    float t = saturate(-gn.z);
    gn.xy += float2(gn.x >= 0.0 ? -t : t, gn.y >= 0.0 ? -t : t);
    float lenSq = dot(gn, gn);
    return lenSq > 0.0 && isfinite(lenSq) ? gn * rsqrt(lenSq) : float3(0.0, 0.0, 1.0);
}

float3 evaluateSkyFallback(float3 dir, float3 horizon, float3 zenith, float ambientFloor)
{
    float skyWeight = smoothstep(-0.12, 0.08, dir.z);
    float3 skyDir = lerp(horizon, zenith, pow(saturate(dir.z), 0.65)) * skyWeight;
    float3 groundBounce = lerp(float3(0.05, 0.045, 0.04), float3(0.10, 0.12, 0.15), saturate(dir.z * 0.5 + 0.5));
    return skyDir + ambientFloor * groundBounce;
}

float4 sampleCascadeVolume(GPUTextureId vol0, GPUTextureId vol1, GPUTextureId vol2, GPUTextureId vol3, uint c, float3 uvw, float mip)
{
    SamplerState s = shared_sampler_linear_repeat();
    if (c == 0u) return gpu_texture<Texture3D<float4>>(vol0.index).SampleLevel(s, uvw, mip);
    if (c == 1u) return gpu_texture<Texture3D<float4>>(vol1.index).SampleLevel(s, uvw, mip);
    if (c == 2u) return gpu_texture<Texture3D<float4>>(vol2.index).SampleLevel(s, uvw, mip);
    return gpu_texture<Texture3D<float4>>(vol3.index).SampleLevel(s, uvw, mip);
}

float computeLocalVoxelSize(World3DVoxelFrameGpu frame, float3 posAnchorRel)
{
    for (uint c = 0u; c < WORLD3D_VOXEL_CASCADE_COUNT; ++c)
    {
        World3DVoxelCascadeGpu cGpu = gpu_load<World3DVoxelCascadeGpu>(frame.Cascades, c);
        float res = float(cGpu.BricksPerAxis * WORLD3D_VOXEL_BRICK_SIZE);
        float3 localV = (posAnchorRel - cGpu.OriginVoxelSize.xyz) / cGpu.OriginVoxelSize.w;
        float margin = 1.5;
        if (any(localV < margin || localV > (res - margin)))
            continue;

        float vSize = cGpu.OriginVoxelSize.w;
        if (c + 1u < WORLD3D_VOXEL_CASCADE_COUNT)
        {
            float3 normD = abs((localV / res) * 2.0 - 1.0);
            float edge = max(normD.x, max(normD.y, normD.z));
            float blend = smoothstep(0.82, 0.97, edge);
            if (blend > 0.001)
            {
                World3DVoxelCascadeGpu nGpu = gpu_load<World3DVoxelCascadeGpu>(frame.Cascades, c + 1u);
                float nRes = float(nGpu.BricksPerAxis * WORLD3D_VOXEL_BRICK_SIZE);
                float3 nLocalV = (posAnchorRel - nGpu.OriginVoxelSize.xyz) / nGpu.OriginVoxelSize.w;
                if (all(nLocalV >= margin && nLocalV <= (nRes - margin)))
                    vSize = lerp(vSize, nGpu.OriginVoxelSize.w, blend);
            }
        }
        return vSize;
    }

    World3DVoxelCascadeGpu cLast = gpu_load<World3DVoxelCascadeGpu>(frame.Cascades, WORLD3D_VOXEL_CASCADE_COUNT - 1u);
    return cLast.OriginVoxelSize.w;
}

bool trySampleClipmap(
    World3DVoxelFrameGpu frame,
    GPUTextureId vol0,
    GPUTextureId vol1,
    GPUTextureId vol2,
    GPUTextureId vol3,
    float3 posAnchorRel,
    float coneDiam,
    out float4 result,
    out float sampledVoxelSize)
{
    for (uint c = 0u; c < WORLD3D_VOXEL_CASCADE_COUNT; ++c)
    {
        World3DVoxelCascadeGpu cGpu = gpu_load<World3DVoxelCascadeGpu>(frame.Cascades, c);
        float res = float(cGpu.BricksPerAxis * WORLD3D_VOXEL_BRICK_SIZE);
        float3 localV = (posAnchorRel - cGpu.OriginVoxelSize.xyz) / cGpu.OriginVoxelSize.w;
        float margin = 1.5;
        if (any(localV < margin || localV > (res - margin)))
            continue;

        float vSize = cGpu.OriginVoxelSize.w;
        float mip = clamp(log2(max(coneDiam / vSize, 1.0)), 0.0, 5.0);
        float3 toroidalV = localV + float3(cGpu.ToroidalOriginX, cGpu.ToroidalOriginY, cGpu.ToroidalOriginZ) * float(WORLD3D_VOXEL_BRICK_SIZE);
        float4 tap = sampleCascadeVolume(vol0, vol1, vol2, vol3, c, toroidalV / res, mip);

        if (c + 1u < WORLD3D_VOXEL_CASCADE_COUNT)
        {
            float3 normD = abs((localV / res) * 2.0 - 1.0);
            float edge = max(normD.x, max(normD.y, normD.z));
            float blend = smoothstep(0.82, 0.97, edge);
            if (blend > 0.001)
            {
                World3DVoxelCascadeGpu nGpu = gpu_load<World3DVoxelCascadeGpu>(frame.Cascades, c + 1u);
                float nRes = float(nGpu.BricksPerAxis * WORLD3D_VOXEL_BRICK_SIZE);
                float3 nLocalV = (posAnchorRel - nGpu.OriginVoxelSize.xyz) / nGpu.OriginVoxelSize.w;
                if (all(nLocalV >= margin && nLocalV <= (nRes - margin)))
                {
                    float nMip = clamp(log2(max(coneDiam / nGpu.OriginVoxelSize.w, 1.0)), 0.0, 5.0);
                    float3 nTor = nLocalV + float3(nGpu.ToroidalOriginX, nGpu.ToroidalOriginY, nGpu.ToroidalOriginZ) * float(WORLD3D_VOXEL_BRICK_SIZE);
                    float4 nTap = sampleCascadeVolume(vol0, vol1, vol2, vol3, c + 1u, nTor / nRes, nMip);
                    tap = lerp(tap, nTap, blend);
                    vSize = lerp(vSize, nGpu.OriginVoxelSize.w, blend);
                }
            }
        }

        result = tap;
        sampledVoxelSize = vSize;
        return true;
    }

    result = float4(0.0);
    sampledVoxelSize = 0.0;
    return false;
}

float4 traceCone(
    World3DVoxelFrameGpu frame,
    GPUTextureId vol0,
    GPUTextureId vol1,
    GPUTextureId vol2,
    GPUTextureId vol3,
    float3 originAnchorRel,
    float3 dir,
    float tanHalfAngle,
    float localVoxelSize,
    float maxDistance,
    uint maxSteps)
{
    float3 accumRgb = float3(0.0);
    float accumA = 0.0;
    float t = localVoxelSize * 1.25;

    for (uint i = 0u; i < maxSteps && t < maxDistance && accumA < 0.96; ++i)
    {
        float diam = max(localVoxelSize, 2.0 * tanHalfAngle * t);
        float3 samplePos = originAnchorRel + dir * t;

        float4 tap;
        float stepVoxel;
        if (!trySampleClipmap(frame, vol0, vol1, vol2, vol3, samplePos, diam, tap, stepVoxel))
            break;

        float w = 1.0 - accumA;
        accumRgb += tap.rgb * w;
        accumA += tap.a * w;
        t += max(max(diam, stepVoxel) * 0.68, stepVoxel * 0.90);
    }

    float visibility = saturate(1.0 - accumA);
    if (visibility > 0.001)
    {
        accumRgb += evaluateSkyFallback(
            dir,
            frame.SkyHorizonAndAmbientFloor.rgb,
            frame.SkyZenithAndFrameIndex.rgb,
            frame.SkyHorizonAndAmbientFloor.a) * visibility;
    }

    return float4(accumRgb, visibility);
}

float3 evaluateRsmNearBounce(
    World3DVoxelFrameGpu frame,
    World3DView view,
    World3DShadowFrameGpu shadowFrame,
    float3 receiverAnchorRel,
    float3 receiverNormal,
    float4 blueNoise,
    float voxelSize0)
{
    if (frame.HasRsm == 0u || shadowFrame.RsmFluxTex.index == 0u || shadowFrame.RsmNormalTex.index == 0u || frame.RsmDepthTex.index == 0u)
        return float3(0.0);

    float rsmExtent = max(shadowFrame.RsmParams.x, 1.0);
    float maxRadiusMeters = clamp(voxelSize0 * 10.0, 1.75, 3.5);
    float searchUvRadius = maxRadiusMeters / rsmExtent;

    float3 originToRsm = float3(view.OriginAnchorCell.xyz - frame.RsmCascade.CascadeCell.xyz) * WORLD3D_CELL_SIZE
        - frame.RsmCascade.CascadeLocal.xyz;
    float3 relRsm = originToRsm + receiverAnchorRel;
    float4 clip = mul(float4(relRsm, 1.0), frame.RsmCascade.LightViewProj);
    float invW = rcp(max(abs(clip.w), 1e-6));
    float2 centerUv = clip.xy * invW * float2(0.5, -0.5) + float2(0.5, 0.5);
    if (any(centerUv <= 0.01 || centerUv >= 0.99))
        return float3(0.0);

    Texture2D<float4> fluxTex = gpu_texture<Texture2D<float4>>(shadowFrame.RsmFluxTex.index);
    Texture2D<float4> normTex = gpu_texture<Texture2D<float4>>(shadowFrame.RsmNormalTex.index);
    Texture2D<float> depthTex = gpu_texture<Texture2D<float>>(frame.RsmDepthTex.index);
    SamplerState s = shared_sampler_linear_clamp();
    SamplerState sDepth = shared_sampler_nearest_clamp();

    float baseAngle = blueNoise.x * 6.2831853;
    float3 accum = float3(0.0);
    [unroll]
    for (uint ti = 0u; ti < 4u; ++ti)
    {
        float f = (float(ti) + blueNoise.y) * 0.25;
        float r = sqrt(f) * searchUvRadius;
        float ang = baseAngle + float(ti) * 2.3999632;
        float2 uv = centerUv + float2(cos(ang), sin(ang)) * r;
        if (any(uv <= 0.005 || uv >= 0.995))
            continue;

        float d = depthTex.SampleLevel(sDepth, uv, 0.0).r;
        if (d <= 0.0 || d >= 0.9999)
            continue;

        float2 sampleNdc = uv * float2(2.0, -2.0) + float2(-1.0, 1.0);
        float4 rsmRelH = mul(float4(sampleNdc, d, 1.0), frame.InvRsmLightViewProj);
        float3 sampleAnchorRel = rsmRelH.xyz / max(abs(rsmRelH.w), 1e-6) - originToRsm;

        float3 delta = sampleAnchorRel - receiverAnchorRel;
        float distSq = dot(delta, delta);
        if (distSq < 1e-4 || distSq > maxRadiusMeters * maxRadiusMeters)
            continue;

        float dist = sqrt(distSq);
        float3 dir = delta / dist;
        float recCos = saturate(dot(receiverNormal, dir));
        if (recCos <= 0.0)
            continue;

        float3 emitterN = normalize(normTex.SampleLevel(s, uv, 0.0).xyz * 2.0 - 1.0);
        float emCos = saturate(dot(emitterN, -dir));
        float nearWindow = 1.0 - smoothstep(0.6, maxRadiusMeters, dist);
        float3 flux = fluxTex.SampleLevel(s, uv, 0.0).rgb;
        float weight = recCos * emCos * nearWindow / (distSq + 0.35);
        accum += flux * (weight * (maxRadiusMeters * maxRadiusMeters) * 0.25);
    }
    return accum;
}

[shader("compute")]
[numthreads(8, 8, 1)]
public void csMain(uint3 id : SV_DispatchThreadID)
{
    VoxelTraceRoot root = gpu_load_root<VoxelTraceRoot>();
    World3DVoxelFrameGpu frame = gpu_load<World3DVoxelFrameGpu>(root.Frame, 0u);
    uint2 traceSize = uint2(frame.TraceWidth, frame.TraceHeight);
    if (any(id.xy >= traceSize))
        return;

    uint2 fullSize = uint2(frame.FullWidth, frame.FullHeight);
    uint frameIdx = uint(frame.SkyZenithAndFrameIndex.w);
    uint2 fullPixel = frame.HalfResMode != 0u
        ? min(id.xy * 2u + uint2((id.x ^ frameIdx) & 1u, (id.y ^ (frameIdx >> 1)) & 1u), fullSize - 1u)
        : id.xy;

    int3 fullCoord = int3(int2(fullPixel), 0);
    Texture2D<float> depthTex = gpu_texture<Texture2D<float>>(root.DepthTex.index);
    float depth = depthTex.Load(fullCoord);

    RWTexture2D<float4> outDiff = gpu_texture<RWTexture2D<float4>>(root.OutDiffuseTex.index);
    RWTexture2D<float4> outSpec = gpu_texture<RWTexture2D<float4>>(root.OutSpecularTex.index);
    if (depth == 0.0)
    {
        outDiff[id.xy] = float4(0.0, 0.0, 0.0, 1.0);
        outSpec[id.xy] = float4(0.0, 0.0, 0.0, 0.0);
        return;
    }

    World3DView view = gpu_load<World3DView>(root.Views, 0u);
    World3DShadowFrameGpu shadowFrame = gpu_load<World3DShadowFrameGpu>(root.ShadowFrame, 0u);

    float2 uv = (float2(fullPixel) + 0.5) * view.ViewportSizeInvSize.zw;
    float2 ndc = uv * float2(2.0, -2.0) + float2(-1.0, 1.0);
    float4 eyeRelativeH = mul(float4(ndc, depth, 1.0), view.CameraInvVP);
    float3 eyeRelPos = eyeRelativeH.xyz / max(eyeRelativeH.w, 1e-6);
    float3 anchorRelPos = eyeRelPos
        + float3(view.EyeCell.xyz - view.OriginAnchorCell.xyz) * WORLD3D_CELL_SIZE
        + view.EyeLocalNear.xyz;

    float4 normalSample = gpu_texture<Texture2D<float4>>(root.NormalTex.index).Load(fullCoord);
    float4 emissiveSample = gpu_texture<Texture2D<float4>>(root.EmissiveTex.index).Load(fullCoord);
    float3 material = gpu_texture<Texture2D<float4>>(root.MaterialTex.index).Load(fullCoord).xyz;

    float3 shadingNormal = normalize(normalSample.xyz * 2.0 - 1.0);
    float3 geomNormal = decodeGeometryNormal(float2(normalSample.a, emissiveSample.a));

    World3DVoxelCascadeGpu c0 = gpu_load<World3DVoxelCascadeGpu>(frame.Cascades, 0u);
    World3DVoxelCascadeGpu cLast = gpu_load<World3DVoxelCascadeGpu>(frame.Cascades, WORLD3D_VOXEL_CASCADE_COUNT - 1u);
    float voxelSize0 = c0.OriginVoxelSize.w;
    float localVoxelSize = computeLocalVoxelSize(frame, anchorRelPos);
    float maxTraceDist = cLast.OriginVoxelSize.w * float(cLast.BricksPerAxis * WORLD3D_VOXEL_BRICK_SIZE) * 0.5;

    float3 viewDir = -normalize(eyeRelPos);
    float3 biasNormal = dot(geomNormal, viewDir) >= 0.0 ? geomNormal : -geomNormal;
    float3 traceOrigin = anchorRelPos + biasNormal * (localVoxelSize * 1.45) + shadingNormal * (localVoxelSize * 0.45);

    Texture2D<float4> bnTex = gpu_texture<Texture2D<float4>>(frame.BlueNoiseTex.index);
    float4 bn = bnTex.Load(int3(int2(id.xy & 63u), 0));
    float angleJitter = frac(bn.x + 0.61803399 * float(frameIdx & 63u)) * 6.2831853;

    float3 upVec = abs(shadingNormal.z) < 0.99 ? float3(0.0, 0.0, 1.0) : float3(1.0, 0.0, 0.0);
    float3 tangent = normalize(cross(upVec, shadingNormal));
    float3 bitangent = cross(shadingNormal, tangent);

    float3 diffAccum = float3(0.0);
    float visAccum = 0.0;
    const float coneTilt = 0.81915204; // sin(55 deg)
    const float coneUp = 0.57357644;   // cos(55 deg)
    const float tanHalfDiffuse = 0.57735027;

    float4 centerCone = traceCone(frame, root.Vol0, root.Vol1, root.Vol2, root.Vol3,
        traceOrigin, shadingNormal, tanHalfDiffuse, localVoxelSize, maxTraceDist, 14u);
    diffAccum += centerCone.rgb * 0.28;
    visAccum += centerCone.a * 0.28;

    [unroll]
    for (uint ci = 0u; ci < 4u; ++ci)
    {
        float phi = angleJitter + float(ci) * 1.5707963;
        float3 localDir = float3(cos(phi) * coneTilt, sin(phi) * coneTilt, coneUp);
        float3 worldDir = tangent * localDir.x + bitangent * localDir.y + shadingNormal * localDir.z;
        float4 cTap = traceCone(frame, root.Vol0, root.Vol1, root.Vol2, root.Vol3,
            traceOrigin, worldDir, tanHalfDiffuse, localVoxelSize, maxTraceDist, 12u);
        diffAccum += cTap.rgb * 0.18;
        visAccum += cTap.a * 0.18;
    }

    diffAccum += evaluateRsmNearBounce(frame, view, shadowFrame, anchorRelPos, shadingNormal, bn, voxelSize0);

    float3 reflDir = reflect(-viewDir, shadingNormal);
    if (dot(reflDir, geomNormal) < 0.0)
        reflDir = normalize(reflDir - geomNormal * dot(reflDir, geomNormal) + geomNormal * 0.05);
    float roughness = clamp(material.y, 0.05, 1.0);
    float specTan = clamp(roughness * roughness * 0.72, 0.025, 0.65);
    float4 specCone = traceCone(frame, root.Vol0, root.Vol1, root.Vol2, root.Vol3,
        traceOrigin, reflDir, specTan, localVoxelSize, maxTraceDist, 14u);

    outDiff[id.xy] = float4(diffAccum, saturate(visAccum));
    outSpec[id.xy] = float4(specCone.rgb, 1.0);
}
