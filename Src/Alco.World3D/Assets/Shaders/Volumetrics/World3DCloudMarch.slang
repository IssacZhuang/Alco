#language slang 2025
module World3DCloudMarch;

import Core;
import World3DSceneData;
import World3DAtmosphere;
import World3DCloudCommon;

[GPURoot]
public struct CloudMarchRoot
{
    public GPUBufferView Views;
    public GPUBufferView CloudFrame;
    public GPUTextureId BaseNoiseTex;
    public GPUTextureId DetailNoiseTex;
    public GPUTextureId DepthTex;
    public GPUTextureId OutRawTex;
    public uint TraceWidth;
    public uint TraceHeight;
};

float cloudInterleavedGradientNoise(float2 pixel, uint frameIndex)
{
    float2 p = pixel + float2(float(frameIndex & 15u) * 5.588238, float((frameIndex >> 2) & 15u) * 3.173921);
    return frac(52.9829189 * frac(dot(p, float2(0.06711056, 0.00583715))));
}

float cloudBayer4x4(uint2 pixel, uint frameIndex)
{
    static const float bayer[16] = {
        0.0 / 16.0,  8.0 / 16.0,  2.0 / 16.0, 10.0 / 16.0,
        12.0 / 16.0, 4.0 / 16.0, 14.0 / 16.0,  6.0 / 16.0,
        3.0 / 16.0, 11.0 / 16.0,  1.0 / 16.0,  9.0 / 16.0,
        15.0 / 16.0, 7.0 / 16.0, 13.0 / 16.0,  5.0 / 16.0
    };
    uint idx = ((pixel.y & 3u) * 4u + (pixel.x & 3u));
    return frac(bayer[idx] + float(frameIndex & 3u) * 0.25);
}

[shader("compute")]
[numthreads(8, 8, 1)]
public void csMain(uint3 id : SV_DispatchThreadID)
{
    CloudMarchRoot root = gpu_load_root<CloudMarchRoot>();
    if (id.x >= root.TraceWidth || id.y >= root.TraceHeight)
        return;

    RWTexture2D<float4> outRaw = gpu_texture<RWTexture2D<float4>>(root.OutRawTex.index);
    World3DCloudFrameGpu cfg = gpu_load<World3DCloudFrameGpu>(root.CloudFrame, 0);
    if ((cfg.Flags & WORLD3D_CLOUD_FLAG_ENABLED) == 0u)
    {
        outRaw[id.xy] = float4(0.0, 0.0, 0.0, 1.0);
        return;
    }

    World3DView view = gpu_load<World3DView>(root.Views, 0);
    uint2 fullDims = max(uint2(view.ViewportSizeInvSize.xy), uint2(1u));
    float2 uv = (float2(id.xy) + 0.5) / float2(float(root.TraceWidth), float(root.TraceHeight));
    float3 ray = cloud_reconstruct_ray(view, uv);

    Texture2D<float> depthTex = gpu_texture<Texture2D<float>>(root.DepthTex.index);
    int2 maxFull = int2(fullDims) - int2(1, 1);
    float2 fullCenter = uv * float2(fullDims);
    float d0 = depthTex.Load(int3(clamp(int2(fullCenter + float2(-0.25, -0.25)), int2(0), maxFull), 0));
    float d1 = depthTex.Load(int3(clamp(int2(fullCenter + float2( 0.25, -0.25)), int2(0), maxFull), 0));
    float d2 = depthTex.Load(int3(clamp(int2(fullCenter + float2(-0.25,  0.25)), int2(0), maxFull), 0));
    float d3 = depthTex.Load(int3(clamp(int2(fullCenter + float2( 0.25,  0.25)), int2(0), maxFull), 0));
    float minDepth = min(min(d0, d1), min(d2, d3));

    float maxDist = cfg.ScatteringParams.w;
    if (minDepth > 0.0)
    {
        float linearZ = cloud_linear_view_z(view, minDepth);
        float2 ndc = uv * float2(2.0, -2.0) + float2(-1.0, 1.0);
        float4 eyePosH = mul(float4(ndc, minDepth, 1.0), view.CameraInvVP);
        float sceneDist = eyePosH.w > 1e-6 ? length(eyePosH.xyz / eyePosH.w) : linearZ;
        maxDist = min(maxDist, sceneDist);
    }

    float tEnter = 0.0;
    float tExit = 0.0;
    if (!cloud_ray_slab(cfg.BasePhaseAndAlt.w, ray.z, cfg.LayerParams.x, cfg.LayerParams.y, maxDist, tEnter, tExit))
    {
        outRaw[id.xy] = float4(0.0, 0.0, 0.0, 1.0);
        return;
    }

    uint steps = clamp(cfg.MaxSteps, 8u, 128u);
    uint lightSteps = clamp(cfg.LightSteps, 1u, 12u);
    float dt = (tExit - tEnter) / float(steps);
    float jitter = frac(cloudBayer4x4(id.xy, cfg.FrameIndex) + cloudInterleavedGradientNoise(float2(id.xy), cfg.FrameIndex) * 0.5);

    Texture3D<float4> baseNoiseTex = gpu_texture<Texture3D<float4>>(root.BaseNoiseTex.index);
    Texture3D<float4> detailNoiseTex = gpu_texture<Texture3D<float4>>(root.DetailNoiseTex.index);

    float3 sunDir = normalize(cfg.SunDirParams.xyz);
    float cosTheta = clamp(dot(ray, sunDir), -1.0, 1.0);
    float phase = cloud_phase_dual(cosTheta, cfg.ScatteringParams.x, cfg.ScatteringParams.y, cfg.ScatteringParams.z);

    float midAltKm = 0.001 * 0.5 * (cfg.LayerParams.x + cfg.LayerParams.y);
    float3 sunBeam = world3d_atmosphere_sun_transmittance(midAltKm, sunDir, cfg.AtmosphereParams)
        * cfg.SunColorParams.rgb * cfg.SunDirParams.w;
    float3 skyAmbient = world3d_sky_color(float3(ray.x, ray.y, max(ray.z, 0.18)), sunDir,
        cfg.AtmosphereParams, cfg.SkyParams, cfg.SunColorParams.rgb, 0.9995, 0.0) * 0.35;

    float thickness = max(cfg.LayerParams.y - cfg.LayerParams.x, 1.0);
    float lightStepLen = (thickness * 0.45) / float(lightSteps);

    float3 scatter = float3(0.0);
    float transmittance = 1.0;

    for (uint i = 0u; i < steps; ++i)
    {
        float t = tEnter + (float(i) + jitter) * dt;
        float3 pos = ray * t;
        float heightFrac;
        float density = cloud_sample_density(cfg, baseNoiseTex, detailNoiseTex, pos, heightFrac);
        if (density <= 1e-4)
            continue;

        float sigma = density * cfg.NoiseParams.z;
        float lightTau = 0.0;
        for (uint ls = 0u; ls < lightSteps; ++ls)
        {
            float3 lPos = pos + sunDir * ((float(ls) + 0.5) * lightStepLen);
            float lh;
            float lDensity = cloud_sample_density(cfg, baseNoiseTex, detailNoiseTex, lPos, lh);
            lightTau += lDensity * lightStepLen * cfg.NoiseParams.z;
        }

        float beer = exp(-lightTau);
        float multiScatter = exp(-lightTau * 0.22) * cfg.NoiseParams.w;
        float powder = lerp(0.55, 1.0, saturate(1.0 - exp(-lightTau * 2.0)));
        float lightEnergy = max(beer * powder, multiScatter);

        float heightAmbient = lerp(0.45, 1.25, saturate(heightFrac));
        float3 stepLighting = sunBeam * (lightEnergy * phase * 1.35) + skyAmbient * heightAmbient;

        float stepTrans = exp(-sigma * dt);
        float3 scatterInteg = stepLighting * (1.0 - stepTrans);
        float aerialFade = exp(-t * 0.000012);
        scatter += transmittance * scatterInteg * aerialFade;
        transmittance *= stepTrans;

        if (transmittance < 0.01)
        {
            transmittance = 0.0;
            break;
        }
    }

    outRaw[id.xy] = float4(scatter, transmittance);
}
