#language slang 2025
module World3DCloudCommon;

import Core;
import SharedSampler;
import World3DSceneData;
import World3DAtmosphere;

public static const uint WORLD3D_CLOUD_FLAG_ENABLED = 1u;
public static const uint WORLD3D_CLOUD_FLAG_CAST_SHADOWS = 2u;
public static const uint WORLD3D_CLOUD_FLAG_HISTORY_VALID = 4u;
public static const uint WORLD3D_CLOUD_FLAG_HALF_RES = 8u;

public static const float WORLD3D_CLOUD_BASE_TILE_METERS = 4096.0;
public static const float WORLD3D_CLOUD_DETAIL_TILE_METERS = 512.0;
public static const float WORLD3D_CLOUD_WEATHER_TILE_METERS = 16384.0;

public struct World3DCloudFrameGpu
{
    public float4 LayerParams;            // x = bottomMeters, y = topMeters, z = coverage, w = density
    public float4 NoiseParams;            // x = detailStrength, y = erosionStrength, z = extinctionScale, w = multiScatterStrength
    public float4 ScatteringParams;       // x = forwardG, y = backG, z = silverLining, w = maxDistanceMeters
    public float4 SunDirParams;           // xyz = directionToSun, w = sunIntensity
    public float4 SunColorParams;         // rgb = sunColor, w = shadowStrength
    public float4 AtmosphereParams;       // atmosphere optics parameters
    public float4 SkyParams;              // sky parameters
    public float4 BasePhaseAndAlt;        // xy = wrapped base UV, z = detail Z drift, w = eyeAltitudeMeters
    public float4 DetailPhaseAndWeather;  // xy = wrapped detail UV, zw = wrapped weather UV
    public float4 ShadowOriginExtent;     // xy = cloudShadowCenterRelEyeXY, z = shadowCoverageMeters, w = invShadowCoverageMeters
    public uint MaxSteps;
    public uint LightSteps;
    public uint FrameIndex;
    public uint Flags;
};

[ForceInline]
public float cloud_remap(float value, float inMin, float inMax, float outMin, float outMax)
{
    float denom = max(inMax - inMin, 1e-5);
    float t = clamp((value - inMin) / denom, 0.0, 1.0);
    return outMin + t * (outMax - outMin);
}

float2 cloudHash22Periodic(float2 p, float period)
{
    float2 q = p - floor(p / period) * period;
    float3 r = frac(float3(q.x, q.y, q.x) * float3(0.1031, 0.1030, 0.0973));
    r += dot(r, r.yzx + 33.33);
    float2 h = frac((r.xx + r.yz) * r.zy);
    float a = h.x * (2.0 * WORLD3D_PI);
    return float2(cos(a), sin(a));
}

float cloudGradientNoise2D(float2 p, float period)
{
    float2 i = floor(p);
    float2 f = p - i;
    float2 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);

    float2 g00 = cloudHash22Periodic(i + float2(0.0, 0.0), period);
    float2 g10 = cloudHash22Periodic(i + float2(1.0, 0.0), period);
    float2 g01 = cloudHash22Periodic(i + float2(0.0, 1.0), period);
    float2 g11 = cloudHash22Periodic(i + float2(1.0, 1.0), period);

    float d00 = dot(g00, f - float2(0.0, 0.0));
    float d10 = dot(g10, f - float2(1.0, 0.0));
    float d01 = dot(g01, f - float2(0.0, 1.0));
    float d11 = dot(g11, f - float2(1.0, 1.0));

    float nx0 = lerp(d00, d10, u.x);
    float nx1 = lerp(d01, d11, u.x);
    return lerp(nx0, nx1, u.y) * 0.5 + 0.5;
}

public float cloud_periodic_weather(float2 weatherUv)
{
    float o0 = cloudGradientNoise2D(weatherUv * 8.0, 8.0);
    float o1 = cloudGradientNoise2D(weatherUv * 16.0, 16.0);
    float o2 = cloudGradientNoise2D(weatherUv * 32.0, 32.0);
    return saturate(o0 * 0.57 + o1 * 0.29 + o2 * 0.14);
}

public float cloud_hg_phase(float cosTheta, float g)
{
    float g2 = g * g;
    float denom = max(1.0 + g2 - 2.0 * g * cosTheta, 1e-4);
    return (1.0 - g2) / (4.0 * WORLD3D_PI * pow(denom, 1.5));
}

public float cloud_phase_dual(float cosTheta, float fwdG, float backG, float silverIntensity)
{
    float fwd = cloud_hg_phase(cosTheta, clamp(fwdG, 0.0, 0.95));
    float back = cloud_hg_phase(cosTheta, clamp(backG, -0.95, 0.0));
    float basePhase = lerp(back, fwd, 0.72);
    float silverPeak = pow(saturate(cosTheta), 10.0) * silverIntensity * 0.65;
    return basePhase + silverPeak;
}

public bool cloud_ray_slab(float originAlt, float dirZ, float bottomAlt, float topAlt, float maxDist,
    out float tEnter, out float tExit)
{
    tEnter = 0.0;
    tExit = 0.0;
    if (bottomAlt >= topAlt || maxDist <= 0.0)
        return false;

    if (abs(dirZ) < 1e-4)
    {
        if (originAlt < bottomAlt || originAlt > topAlt)
            return false;
        tEnter = 0.0;
        tExit = maxDist;
        return tExit > 0.01;
    }

    float invDirZ = rcp(dirZ);
    float t0 = (bottomAlt - originAlt) * invDirZ;
    float t1 = (topAlt - originAlt) * invDirZ;
    float nearT = min(t0, t1);
    float farT = max(t0, t1);
    tEnter = max(nearT, 0.0);
    tExit = min(farT, maxDist);
    return tExit > tEnter + 0.01;
}

public float cloud_sample_density(
    World3DCloudFrameGpu cfg,
    Texture3D<float4> baseNoiseTex,
    Texture3D<float4> detailNoiseTex,
    float3 eyeRelPos,
    out float heightFrac)
{
    float cloudBottom = cfg.LayerParams.x;
    float cloudTop = cfg.LayerParams.y;
    float thickness = max(cloudTop - cloudBottom, 1.0);
    float worldAlt = cfg.BasePhaseAndAlt.w + eyeRelPos.z;
    heightFrac = (worldAlt - cloudBottom) / thickness;
    if (heightFrac <= 0.0 || heightFrac >= 1.0 || cfg.LayerParams.z <= 0.001 || cfg.LayerParams.w <= 1e-5)
        return 0.0;

    float heightProfile = smoothstep(0.0, 0.14, heightFrac) * (1.0 - smoothstep(0.58, 1.0, heightFrac));
    if (heightProfile <= 0.001)
        return 0.0;

    SamplerState repeatSampler = shared_sampler_linear_repeat();
    float2 weatherUv = cfg.DetailPhaseAndWeather.zw + eyeRelPos.xy * (1.0 / WORLD3D_CLOUD_WEATHER_TILE_METERS);
    float weather = cloud_periodic_weather(weatherUv);

    float3 baseUv = float3(
        cfg.BasePhaseAndAlt.xy + eyeRelPos.xy * (1.0 / WORLD3D_CLOUD_BASE_TILE_METERS),
        heightFrac * 0.85 + cfg.BasePhaseAndAlt.z * 0.1);
    float4 baseSample = baseNoiseTex.SampleLevel(repeatSampler, baseUv, 0.0);
    float baseFbm = dot(baseSample.gba, float3(0.625, 0.25, 0.125));
    float baseShape = saturate(cloud_remap(baseSample.r, (1.0 - baseFbm) * 0.52, 1.0, 0.0, 1.0));

    float localCov = clamp(cfg.LayerParams.z * (0.45 + 0.95 * weather), 0.0, 1.0);
    float shaped = saturate(cloud_remap(baseShape * heightProfile, 1.0 - localCov, 1.0, 0.0, 1.0));
    if (shaped <= 0.001)
        return 0.0;

    float3 detailUv = float3(
        cfg.DetailPhaseAndWeather.xy + eyeRelPos.xy * (1.0 / WORLD3D_CLOUD_DETAIL_TILE_METERS),
        heightFrac * 1.4 + cfg.BasePhaseAndAlt.z * 0.2);
    float4 detailSample = detailNoiseTex.SampleLevel(repeatSampler, detailUv, 0.0);
    float detailFbm = dot(detailSample.rgb, float3(0.625, 0.25, 0.125));
    float detailMod = lerp(detailFbm, 1.0 - detailFbm, saturate(heightFrac * 1.5));
    float erode = detailMod * cfg.NoiseParams.x * cfg.NoiseParams.y * 0.65;
    float finalDensity = saturate(cloud_remap(shaped, erode, 1.0, 0.0, 1.0));
    return finalDensity * cfg.LayerParams.w;
}

public float3 cloud_reconstruct_ray(World3DView view, float2 uv)
{
    float2 ndc = uv * float2(2.0, -2.0) + float2(-1.0, 1.0);
    float4 clipNear = mul(float4(ndc, 0.0, 1.0), view.CameraInvVP);
    return normalize(clipNear.xyz);
}

public float cloud_linear_view_z(World3DView view, float depth)
{
    return view.EyeLocalNear.w / max(depth, 1e-6);
}
