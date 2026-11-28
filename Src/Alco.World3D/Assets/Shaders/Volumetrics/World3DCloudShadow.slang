#language slang 2025
module World3DCloudShadow;

import Core;
import World3DCloudCommon;

[GPURoot]
public struct CloudShadowRoot
{
    public GPUBufferView CloudFrame;
    public GPUTextureId BaseNoiseTex;
    public GPUTextureId DetailNoiseTex;
    public GPUTextureId OutShadowTex;
    public uint Resolution;
    public uint Reserved0;
    public uint Reserved1;
    public uint Reserved2;
};

[shader("compute")]
[numthreads(8, 8, 1)]
public void csMain(uint3 id : SV_DispatchThreadID)
{
    CloudShadowRoot root = gpu_load_root<CloudShadowRoot>();
    if (any(id.xy >= root.Resolution.xx))
        return;

    RWTexture2D<float> outShadow = gpu_texture<RWTexture2D<float>>(root.OutShadowTex.index);
    World3DCloudFrameGpu cfg = gpu_load<World3DCloudFrameGpu>(root.CloudFrame, 0);
    if ((cfg.Flags & WORLD3D_CLOUD_FLAG_ENABLED) == 0u
        || (cfg.Flags & WORLD3D_CLOUD_FLAG_CAST_SHADOWS) == 0u)
    {
        outShadow[id.xy] = 1.0;
        return;
    }

    float3 sunDir = normalize(cfg.SunDirParams.xyz);
    if (sunDir.z <= 0.01)
    {
        outShadow[id.xy] = 1.0;
        return;
    }

    float2 uv = (float2(id.xy) + 0.5) / float(root.Resolution);
    float2 hitOffsetXY = cfg.ShadowOriginExtent.xy + (uv - 0.5) * cfg.ShadowOriginExtent.z;

    float cloudBottom = cfg.LayerParams.x;
    float cloudTop = cfg.LayerParams.y;
    float thickness = max(cloudTop - cloudBottom, 1.0);
    float3 marchDir = sunDir / max(sunDir.z, 0.08);
    float marchLen = thickness * length(marchDir);

    const uint ShadowSteps = 12u;
    float dt = thickness / float(ShadowSteps);
    float ds = marchLen / float(ShadowSteps);
    float3 startEyeRel = float3(hitOffsetXY, cloudBottom - cfg.BasePhaseAndAlt.w);

    Texture3D<float4> baseNoiseTex = gpu_texture<Texture3D<float4>>(root.BaseNoiseTex.index);
    Texture3D<float4> detailNoiseTex = gpu_texture<Texture3D<float4>>(root.DetailNoiseTex.index);

    float tau = 0.0;
    for (uint s = 0u; s < ShadowSteps; ++s)
    {
        float t = (float(s) + 0.5) * dt;
        float3 samplePos = startEyeRel + marchDir * t;
        float h;
        float density = cloud_sample_density(cfg, baseNoiseTex, detailNoiseTex, samplePos, h);
        tau += density * ds * cfg.NoiseParams.z;
        if (tau > 8.0)
            break;
    }

    float rawTrans = exp(-tau * (0.45 + cfg.SunColorParams.w * 1.45));
    float shadow = lerp(1.0, max(rawTrans, 0.08), cfg.SunColorParams.w);
    float sunHorizon = smoothstep(0.01, 0.16, sunDir.z);
    outShadow[id.xy] = lerp(1.0, shadow, sunHorizon);
}
