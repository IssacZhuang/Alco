#language slang 2025
module World3DVoxelInject;

import Core;
import SharedSampler;
import World3DSceneData;
import World3DShadowSample;
import World3DVoxelCommon;

[GPURoot]
public struct VoxelInjectRoot
{
    public GPUBufferView Frame;
    public GPUBufferView Views;
    public GPUBufferView ShadowFrame;
    public GPUTextureId Vol0;
    public GPUTextureId Vol1;
    public GPUTextureId Vol2;
    public GPUTextureId Vol3;
    public uint DynamicMode;
    public uint Reserved0;
};

float4 readVolume(GPUTextureId vol0, GPUTextureId vol1, GPUTextureId vol2, GPUTextureId vol3, uint c, uint3 coord)
{
    if (c == 0u) return gpu_texture<RWTexture3D<float4>>(vol0.index)[coord];
    if (c == 1u) return gpu_texture<RWTexture3D<float4>>(vol1.index)[coord];
    if (c == 2u) return gpu_texture<RWTexture3D<float4>>(vol2.index)[coord];
    return gpu_texture<RWTexture3D<float4>>(vol3.index)[coord];
}

void writeVolume(GPUTextureId vol0, GPUTextureId vol1, GPUTextureId vol2, GPUTextureId vol3, uint c, uint3 coord, float4 val)
{
    if (c == 0u) gpu_texture<RWTexture3D<float4>>(vol0.index)[coord] = val;
    else if (c == 1u) gpu_texture<RWTexture3D<float4>>(vol1.index)[coord] = val;
    else if (c == 2u) gpu_texture<RWTexture3D<float4>>(vol2.index)[coord] = val;
    else gpu_texture<RWTexture3D<float4>>(vol3.index)[coord] = val;
}

[shader("compute")]
[numthreads(64, 1, 1)]
public void csMain(uint3 groupId : SV_GroupID, uint3 localId : SV_GroupThreadID)
{
    VoxelInjectRoot root = gpu_load_root<VoxelInjectRoot>();
    World3DVoxelFrameGpu frame = gpu_load<World3DVoxelFrameGpu>(root.Frame, 0u);

    uint poolCapacity = root.DynamicMode != 0u ? frame.DynamicCapacity : frame.StaticCapacity;
    uint poolIdx = groupId.x;
    if (poolIdx >= poolCapacity)
        return;

    if (root.DynamicMode != 0u)
    {
        uint dynUsed = min(gpu_load<uint>(frame.Counters, VOXEL_COUNTER_DYNAMIC_USED), poolCapacity);
        if (poolIdx >= dynUsed)
            return;
    }

    GPUBufferView reverseTable = root.DynamicMode != 0u ? frame.DynamicReverseTable : frame.StaticReverseTable;
    GPUBufferView brickPool = root.DynamicMode != 0u ? frame.DynamicBrickPool : frame.StaticBrickPool;

    uint pageSlot = gpu_load<uint>(reverseTable, poolIdx);
    if (pageSlot == WORLD3D_VOXEL_INVALID_INDEX)
        return;

    uint cascade;
    uint3 toroidalBrick = voxel_decode_slot(pageSlot, frame.BricksPerAxis, cascade);
    if (cascade >= WORLD3D_VOXEL_CASCADE_COUNT)
        return;

    World3DView view = gpu_load<World3DView>(root.Views, 0u);
    World3DShadowFrameGpu shadowFrame = gpu_load<World3DShadowFrameGpu>(root.ShadowFrame, 0u);
    World3DVoxelCascadeGpu cGpu = gpu_load<World3DVoxelCascadeGpu>(frame.Cascades, cascade);
    uint3 localBrick = voxel_toroidal_to_local(toroidalBrick, cGpu);
    float voxelSize = cGpu.OriginVoxelSize.w;
    float3 anchorToEye = float3(view.OriginAnchorCell.xyz - view.EyeCell.xyz) * WORLD3D_CELL_SIZE - view.EyeLocalNear.xyz;

    uint baseVoxel = poolIdx * WORLD3D_VOXEL_BRICK_UINT4_STRIDE;
    for (uint k = 0u; k < 8u; ++k)
    {
        uint v = localId.x * 8u + k;
        uint4 packed = gpu_load<uint4>(brickPool, baseVoxel + v);

        float3 albedo;
        float3 normalWS;
        float3 emissive;
        float opacity;
        if (!voxel_decode_accum(packed, albedo, normalWS, emissive, opacity))
            continue;

        uint3 subVoxel = uint3(v & 7u, (v >> 3) & 7u, v >> 6);
        uint3 texelCoord = toroidalBrick * WORLD3D_VOXEL_BRICK_SIZE + subVoxel;
        float3 localVoxelPos = float3(localBrick * WORLD3D_VOXEL_BRICK_SIZE + subVoxel) + 0.5;
        float3 anchorRelPos = cGpu.OriginVoxelSize.xyz + localVoxelPos * voxelSize;
        float3 eyeRelPos = anchorRelPos + anchorToEye;

        bool hasDir = dot(normalWS, normalWS) > 0.01;
        float3 sunEvalNormal = hasDir ? normalWS : frame.SunDirIntensity.xyz;
        float ndotL = hasDir ? saturate(dot(normalWS, frame.SunDirIntensity.xyz)) : 0.65;

        float3 directIrradiance = float3(0.0);
        if (ndotL > 0.0 && frame.SunDirIntensity.w > 0.0)
        {
            float3 shadowEyeRel = eyeRelPos + sunEvalNormal * (voxelSize * 0.55) + frame.SunDirIntensity.xyz * (voxelSize * 0.25);
            float sunVis = world3d_sample_sun_shadow_fast(shadowFrame, view, shadowEyeRel, sunEvalNormal);
            directIrradiance += frame.SunColor.rgb * (frame.SunDirIntensity.w * ndotL * sunVis);
        }

        for (uint li = 0u; li < frame.PointLightCount; ++li)
        {
            World3DPointLightGpu light = gpu_load<World3DPointLightGpu>(frame.PointLights, li);
            float3 lAnchorRel = float3(light.Cell.xyz - view.OriginAnchorCell.xyz) * WORLD3D_CELL_SIZE
                + light.LocalPositionAndRadius.xyz;
            float3 delta = lAnchorRel - anchorRelPos;
            float distSq = dot(delta, delta);
            float radius = max(light.LocalPositionAndRadius.w, 0.01);
            float radiusSq = radius * radius;
            if (distSq >= radiusSq)
                continue;

            float win = saturate(1.0 - distSq / radiusSq);
            win *= win;
            float atten = win / (distSq + 0.25);
            float3 lDir = delta * rsqrt(max(distSq, 1e-6));
            float pNdotL = hasDir ? saturate(dot(normalWS, lDir)) : 0.65;
            directIrradiance += light.ColorAndIntensity.rgb * (light.ColorAndIntensity.w * atten * pNdotL);
        }

        float3 reflected = albedo * (1.0 / 3.14159265) * directIrradiance;
        float3 combined = reflected + emissive * frame.EmissiveBoost;

        if (frame.HasRsm != 0u && shadowFrame.RsmFluxTex.index != 0u && frame.RsmDepthTex.index != 0u)
        {
            float3 relRsm = float3(view.OriginAnchorCell.xyz - frame.RsmCascade.CascadeCell.xyz) * WORLD3D_CELL_SIZE
                - frame.RsmCascade.CascadeLocal.xyz
                + anchorRelPos;
            float4 clip = mul(float4(relRsm, 1.0), frame.RsmCascade.LightViewProj);
            float invW = rcp(max(abs(clip.w), 1e-6));
            float2 uv = clip.xy * invW * float2(0.5, -0.5) + float2(0.5, 0.5);
            float d = clip.z * invW;
            if (uv.x > 0.01 && uv.x < 0.99 && uv.y > 0.01 && uv.y < 0.99 && d > 0.0 && d < 1.0)
            {
                Texture2D<float> rsmDepthTex = gpu_texture<Texture2D<float>>(frame.RsmDepthTex.index);
                float rsmD = rsmDepthTex.SampleLevel(shared_sampler_nearest_clamp(), uv, 0.0).r;
                if (rsmD < 0.9999 && abs(d - rsmD) * frame.RsmDepthRange <= voxelSize * 1.75)
                {
                    Texture2D<float4> rsmFluxTex = gpu_texture<Texture2D<float4>>(shadowFrame.RsmFluxTex.index);
                    float3 rsmFlux = rsmFluxTex.SampleLevel(shared_sampler_linear_clamp(), uv, 0.0).rgb;
                    combined = max(combined, rsmFlux);
                }
            }
        }

        float3 totalRadiance = combined * opacity;
        if (root.DynamicMode != 0u)
        {
            float4 prev = readVolume(root.Vol0, root.Vol1, root.Vol2, root.Vol3, cascade, texelCoord);
            totalRadiance = lerp(prev.rgb, totalRadiance, opacity);
            opacity = max(prev.a, opacity);
        }
        writeVolume(root.Vol0, root.Vol1, root.Vol2, root.Vol3, cascade, texelCoord, float4(totalRadiance, opacity));
    }
}
