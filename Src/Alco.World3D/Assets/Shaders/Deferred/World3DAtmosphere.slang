#language slang 2025
module World3DAtmosphere;

public static const float WORLD3D_PI = 3.14159265358979323846;

// Kilometers, +Z up. Keep the optical constants in sync with ProceduralSkyUtility.
static const float EarthRadius = 6360.0;
static const float AtmosphereRadius = 6420.0;
static const float RayleighHeight = 8.0;
static const float MieHeight = 1.2;
static const float ViewHeight = 0.2;
static const float3 BetaRayleigh = float3(5.5e-3, 13.0e-3, 22.4e-3);
static const float BetaMie = 21.0e-3;
static const float3 BetaOzone = float3(0.650e-3, 1.881e-3, 0.085e-3);

float2 atmosphereRaySphere(float3 origin, float3 direction, float radius)
{
    float b = dot(origin, direction);
    float discriminant = b * b - (dot(origin, origin) - radius * radius);
    if (discriminant < 0.0)
        return float2(-1.0, -1.0);

    float distance = sqrt(discriminant);
    return float2(-b - distance, -b + distance);
}

float2 atmosphereDensity(float3 position)
{
    float height = length(position) - EarthRadius;
    return exp(-height / float2(RayleighHeight, MieHeight));
}

float2 atmosphereSunDensity(float3 position, float3 directionToSun)
{
    if (atmosphereRaySphere(position, directionToSun, EarthRadius).x > 0.0)
        return float2(-1.0, -1.0);

    const int SampleCount = 8;
    float stepLength = atmosphereRaySphere(position, directionToSun, AtmosphereRadius).y / SampleCount;
    float2 integral = 0.0;
    for (int i = 0; i < SampleCount; ++i)
        integral += atmosphereDensity(position + directionToSun * ((i + 0.5) * stepLength)) * stepLength;
    return integral;
}

float atmosphereNightFactor(float3 directionToSun)
{
    return 1.0 - smoothstep(-0.08, 0.06, directionToSun.z);
}

// Visible single scattering (Rayleigh, Mie, and ozone extinction), ported from AlcoWorld3D_Atmosphere.
float3 atmosphereRadiance(float3 ray, float3 directionToSun, float4 atmosphere, float nightFloor, float solarRadiance)
{
    const float3 origin = float3(0.0, 0.0, EarthRadius + ViewHeight);

    // March at or above the horizon to avoid a discontinuity at the earth limb.
    // A straight-down ray has no horizontal direction after the clamp.
    float3 march = float3(ray.xy, max(ray.z, 0.0));
    float lengthSquared = dot(march, march);
    float3 marchDirection = lengthSquared > 1e-12 ? march * rsqrt(lengthSquared) : float3(1.0, 0.0, 0.0);

    float cosTheta = clamp(dot(ray, directionToSun), -1.0, 1.0);
    float g = atmosphere.z;
    float g2 = g * g;
    float phaseRayleigh = (3.0 / (16.0 * WORLD3D_PI)) * (1.0 + cosTheta * cosTheta);
    float phaseMie = (1.0 - g2) / (4.0 * WORLD3D_PI * pow(1.0 + g2 - 2.0 * g * cosTheta, 1.5));
    float3 betaRayleigh = BetaRayleigh * atmosphere.x;
    float betaMie = BetaMie * atmosphere.y;

    const int SampleCount = 16;
    float stepLength = atmosphereRaySphere(origin, marchDirection, AtmosphereRadius).y / SampleCount;
    float3 rayleighSum = 0.0;
    float3 mieSum = 0.0;
    float2 viewDensity = 0.0;
    for (int i = 0; i < SampleCount; ++i)
    {
        float3 position = origin + marchDirection * ((i + 0.5) * stepLength);
        float2 localDensity = atmosphereDensity(position) * stepLength;
        viewDensity += localDensity;

        float2 sunDensity = atmosphereSunDensity(position, directionToSun);
        if (sunDensity.x < 0.0)
            continue;

        float2 density = viewDensity + sunDensity;
        float3 transmittance = exp(-((betaRayleigh + BetaOzone) * density.x + betaMie * 1.1 * density.y));
        rayleighSum += localDensity.x * transmittance;
        mieSum += localDensity.y * transmittance;
    }

    float3 radiance = solarRadiance * (rayleighSum * betaRayleigh * phaseRayleigh + mieSum * betaMie * phaseMie);

    // Blend toward darker, wavelength-dependent ground haze below the horizon.
    float ground = smoothstep(0.0, 1.0, (-ray.z + 0.08) / 0.35);
    float3 groundMix = float3(ground * ground, ground * (2.0 - ground), 1.0 - pow(1.0 - ground, 3.0));
    radiance = lerp(radiance, radiance * 0.55, lerp(ground.xxx, groundMix, 0.75));
    radiance += nightFloor * atmosphereNightFactor(directionToSun) * float3(0.5, 0.7, 1.0);
    return radiance * atmosphere.w;
}

float3 atmosphereHash33(float3 p)
{
    p = frac(p * float3(0.1031, 0.1030, 0.0973));
    p += dot(p, p.yxz + 33.33);
    return frac((p.xxy + p.yxx) * p.zyx);
}

float atmosphereHash13(float3 p)
{
    p = frac(p * 0.1031);
    p += dot(p, p.zyx + 31.32);
    return frac((p.x + p.y) * p.z);
}

float3 atmosphereStars(float3 ray, float3 directionToSun, float intensity)
{
    float visibility = atmosphereNightFactor(directionToSun) * smoothstep(-0.02, 0.2, ray.z) * intensity;
    if (visibility <= 0.0)
        return 0.0;

    float3 p = ray * 90.0;
    float3 cell = floor(p);
    if (atmosphereHash13(cell) < 0.93)
        return 0.0;

    float3 starPosition = (atmosphereHash33(cell + 17.17) - 0.5) * 0.85;
    float core = 1.0 - smoothstep(0.0, 0.16, length(frac(p) - 0.5 - starPosition));
    float magnitude = pow(atmosphereHash13(cell + 7.31), 5.0);
    float3 tint = lerp(float3(0.65, 0.8, 1.0), float3(1.0, 0.85, 0.65), atmosphereHash13(cell + 3.77));
    return tint * (core * core * (0.15 + magnitude) * visibility * 2.0);
}

float atmosphereSunDisc(float3 ray, float3 directionToSun, float cosRadius)
{
    float sunDot = dot(ray, directionToSun);
    float edgeWidth = max((1.0 - cosRadius) * 0.2, 1e-7);
    float disc = smoothstep(cosRadius - edgeWidth, cosRadius, sunDot);
    float corona = smoothstep(1.0 - (1.0 - cosRadius) * 3.5, cosRadius, sunDot) - disc;
    float horizon = saturate((directionToSun.z + 0.1) * 10.0);
    horizon *= horizon;
    return (disc + corona * 0.025) * smoothstep(0.0, 1.0, horizon);
}

// Direct solar beam transmittance at a given altitude in km above sea level.
public float3 world3d_atmosphere_sun_transmittance(float altitudeKm, float3 directionToSun, float4 atmosphere)
{
    float3 position = float3(0.0, 0.0, EarthRadius + max(altitudeKm, 0.01));
    float2 sunDensity = atmosphereSunDensity(position, directionToSun);
    if (sunDensity.x < 0.0)
        return float3(0.0, 0.0, 0.0);

    float3 betaRayleigh = BetaRayleigh * atmosphere.x;
    float betaMie = BetaMie * atmosphere.y;
    return exp(-((betaRayleigh + BetaOzone) * sunDensity.x + betaMie * 1.1 * sunDensity.y));
}

// The filtered ambient gradient deliberately excludes the visible disc and stars.
public float3 world3d_sky_color(float3 ray, float3 directionToSun, float4 atmosphere, float4 sky,
    float3 sunColor, float discCosRadius, float discBrightness)
{
    float3 radiance = atmosphereRadiance(ray, directionToSun, atmosphere, sky.y, sky.z)
        + atmosphereStars(ray, directionToSun, sky.x);
    if (discBrightness > 0.0)
        radiance += sunColor * discBrightness * atmosphereSunDisc(ray, directionToSun, discCosRadius);
    return radiance;
}
