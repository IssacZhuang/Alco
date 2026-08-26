// file "AlcoWorld3D_Atmosphere.slang" - module "AlcoWorld3D_Atmosphere"
//
// Explicit set/binding declarations; bindings within a set are assigned in
// declaration order and are compiler-owned.
// Lib declarations are public so importing modules see them.

#language slang 2025
module AlcoWorld3D_Atmosphere;

import AlcoRendering_Core;

// Physically-based procedural sky: single-scattering atmosphere (Rayleigh +
// Mie) over a spherical earth, plus a hash-based procedural star field for the
// night. Uses PI from AlcoRendering_Core.
//
// The model follows the classic GPU Gems 2 / Hillaire formulation: density
// falls off exponentially with height (separate scale heights for Rayleigh
// and Mie), single scattering is integrated by ray marching, and the sun
// transmittance at each sample is integrated analytically by a second short
// march. No lookup textures are required, so the sky is a pure function of
// the view direction and the sun direction.
//
// Distances are in kilometers. All directions are normalized, +Z is up
// (the sun direction is the direction TOWARD the sun, i.e. -sunDirection of
// the lighting cbuffer).
//
// The C# twin of the sun transmittance math lives in
// Alco.Rendering.ProceduralSkyUtility; the atmosphere constants below MUST
// stay in sync with it.
//
// Parameter packing (identical in DeferredLighting.slang and
// AlcoWorld3D_VoxelCommon.slang):
//   skyParams:  x = rayleigh density scale (1 = earth default)
//               y = mie density scale (1 = default, raise for haze)
//               z = mie phase anisotropy g (0.8 default)
//               w = sky exposure scale
//   skyParams2: x = star intensity (used by atmosphereStars only)
//               y = night floor radiance (faint ambient after dark)
//               z = solar radiance scale driving the scattering integral
//               w = unused

public static const float ATM_EARTH_RADIUS = 6360.0;      // km
public static const float ATM_ATMOSPHERE_RADIUS = 6420.0; // km
public static const float ATM_H_RAYLEIGH = 8.0;           // rayleigh scale height, km
public static const float ATM_H_MIE = 1.2;                // mie scale height, km
public static const float3 ATM_BETA_RAYLEIGH = float3(5.5e-3, 13.0e-3, 22.4e-3); // 1/km
public static const float ATM_BETA_MIE = 21.0e-3;         // 1/km (extinction is 1.1x)
// Ozone absorption (Chappuis bands); approximated with the rayleigh scale
// height so it can share the rayleigh density integral. It reddens sunsets,
// but note that single scattering without multiple scattering still leaves a
// teal cast in the anti-sun twilight band (a known limitation of the model).
public static const float3 ATM_BETA_OZONE = float3(0.650e-3, 1.881e-3, 0.085e-3); // 1/km
public static const float ATM_VIEW_HEIGHT = 0.2;          // viewer height above ground, km

// Both intersection distances of ray (origin, dir) with a sphere of the given
// radius centered at the origin; (-1, -1) when the ray misses.
public float2 atmosphereRaySphere(float3 origin, float3 dir, float radius)
{
    float b = dot(origin, dir);
    float c = dot(origin, origin) - radius * radius;
    float discriminant = b * b - c;
    if (discriminant < 0.0)
    {
        return float2(-1.0, -1.0);
    }
    float sd = sqrt(discriminant);
    return float2(-b - sd, -b + sd);
}

// Density integral (km, multiply by beta for optical depth) from pos toward
// the sun until the ray leaves the atmosphere. Returns (-1, -1) when the
// earth occludes the sun. x = rayleigh, y = mie.
public float2 atmosphereSunDensityIntegral(float3 pos, float3 dirToSun, int sampleCount)
{
    if (atmosphereRaySphere(pos, dirToSun, ATM_EARTH_RADIUS).x > 0.0)
    {
        return float2(-1.0, -1.0);
    }

    float tEnd = atmosphereRaySphere(pos, dirToSun, ATM_ATMOSPHERE_RADIUS).y;
    float dt = tEnd / (float)sampleCount;
    float2 integral = 0.0;
    for (int i = 0; i < sampleCount; i++)
    {
        float3 p = pos + dirToSun * (((float)i + 0.5) * dt);
        float height = length(p) - ATM_EARTH_RADIUS;
        integral += float2(exp(-height / ATM_H_RAYLEIGH), exp(-height / ATM_H_MIE)) * dt;
    }
    return integral;
}

// Single-scattering sky radiance for a view ray (HDR, linear). viewSamples /
// sunSamples trade quality for speed: use (16, 8) for the sky itself and
// (8, 4) for ambient / GI queries evaluated many times per pixel.
public float3 atmosphereSkyRadiance(float3 rayDir, float3 dirToSun, float4 skyParams, float4 skyParams2, int viewSamples, int sunSamples)
{
    float3 origin = float3(0.0, 0.0, ATM_EARTH_RADIUS + ATM_VIEW_HEIGHT);

    // March along the view ray clamped to the horizon. A horizon-clamped ray
    // from ATM_VIEW_HEIGHT always misses the earth, so the march runs to the
    // atmosphere exit and the radiance is continuous in view direction. The
    // raw ground-hit branch used to make the optical path jump from the
    // ~875 km tangent traversal to ~25 km at the earth limb (~0.45 deg below
    // the horizon), which drew as a hard line; the below-horizon appearance
    // is instead shaped by the ground blend at the end of this function
    // (Complementary Unbound style).
    float3 marchDir = normalize(float3(rayDir.x, rayDir.y, max(rayDir.z, 0.0)));

    float2 atmHit = atmosphereRaySphere(origin, marchDir, ATM_ATMOSPHERE_RADIUS);
    float tStart = max(atmHit.x, 0.0);
    float tEnd = atmHit.y;

    float cosTheta = dot(rayDir, dirToSun);
    float phaseRayleigh = 3.0 / (16.0 * PI) * (1.0 + cosTheta * cosTheta);
    float g = skyParams.z;
    float g2 = g * g;
    float phaseMie = (1.0 - g2) / (4.0 * PI * pow(1.0 + g2 - 2.0 * g * cosTheta, 1.5));

    float3 betaRayleigh = ATM_BETA_RAYLEIGH * skyParams.x;
    float betaMie = ATM_BETA_MIE * skyParams.y;

    float dt = (tEnd - tStart) / (float)viewSamples;
    float3 rayleighSum = 0.0;
    float3 mieSum = 0.0;
    float2 viewDensity = 0.0;
    for (int i = 0; i < viewSamples; i++)
    {
        float3 p = origin + marchDir * (tStart + ((float)i + 0.5) * dt);
        float height = length(p) - ATM_EARTH_RADIUS;
        float2 dLocal = float2(exp(-height / ATM_H_RAYLEIGH), exp(-height / ATM_H_MIE)) * dt;
        viewDensity += dLocal;

        float2 sunDensity = atmosphereSunDensityIntegral(p, dirToSun, sunSamples);
        if (sunDensity.x < 0.0)
        {
            continue; // this sample is in the earth's shadow
        }
        float3 tau = betaRayleigh * (viewDensity.x + sunDensity.x)
            + betaMie * 1.1 * (viewDensity.y + sunDensity.y)
            + ATM_BETA_OZONE * (viewDensity.x + sunDensity.x);
        float3 transmittance = exp(-tau);
        rayleighSum += dLocal.x * transmittance;
        mieSum += dLocal.y * transmittance;
    }

    float3 radiance = skyParams2.z
        * (rayleighSum * betaRayleigh * phaseRayleigh + mieSum * betaMie * phaseMie);

    // Below the horizon, blend the (now constant) horizon radiance toward a
    // darkened ground haze over a smooth window that straddles the horizon
    // (view Z +0.08 .. -0.27). The smoothstep has zero slope at both ends and
    // the per-channel mix rates fake wavelength-dependent ground scattering,
    // so no seam can appear at the horizon line.
    float groundT = smoothstep(0.0, 1.0, saturate((-rayDir.z + 0.08) / 0.35));
    float3 groundColor = radiance * 0.55;
    float3 groundMix = float3(groundT * groundT,
                              groundT * (2.0 - groundT),
                              1.0 - pow(1.0 - groundT, 3.0));
    radiance = lerp(radiance, groundColor, lerp(groundT.xxx, groundMix, 0.75));

    // Night floor: a faint blue ambient so the sky never goes fully black.
    float night = 1.0 - smoothstep(-0.08, 0.06, dirToSun.z);
    radiance += skyParams2.y * night * float3(0.5, 0.7, 1.0);

    return radiance * skyParams.w;
}

// ------------------------------------------------------------- star field ---

public float3 atmosphereHash33(float3 p)
{
    p = frac(p * float3(0.1031, 0.1030, 0.0973));
    p += dot(p, p.yxz + 33.33);
    return frac((p.xxy + p.yxx) * p.zyx);
}

public float atmosphereHash13(float3 p)
{
    p = frac(p * 0.1031);
    p += dot(p, p.zyx + 31.32);
    return frac((p.x + p.y) * p.z);
}

// Procedural star field (HDR, linear). Stars fade in as the sun drops below
// the horizon and are extinguished near the horizon line. Not for GI queries:
// point stars would turn into noise under cone tracing.
public float3 atmosphereStars(float3 rayDir, float3 dirToSun, float intensity)
{
    float night = 1.0 - smoothstep(-0.08, 0.06, dirToSun.z);
    float extinction = smoothstep(-0.02, 0.2, rayDir.z);
    if (night * extinction * intensity <= 0.0)
    {
        return 0.0;
    }

    // Cells on a 3D grid projected through the direction vector.
    float3 p = rayDir * 90.0;
    float3 cell = floor(p);
    float3 f = frac(p) - 0.5;
    if (atmosphereHash13(cell) < 0.93)
    {
        return 0.0; // only ~7% of cells hold a star
    }

    float3 starPos = (atmosphereHash33(cell + 17.17) - 0.5) * 0.85;
    float d = length(f - starPos);
    float core = smoothstep(0.16, 0.0, d);
    // Magnitude distribution: many dim stars, few bright ones.
    float magnitude = pow(atmosphereHash13(cell + 7.31), 5.0);
    float brightness = core * core * (0.15 + magnitude);
    // Subtle temperature tint per star (blue-white to orange).
    float3 tint = lerp(float3(0.65, 0.8, 1.0), float3(1.0, 0.85, 0.65), atmosphereHash13(cell + 3.77));
    return tint * brightness * intensity * night * extinction * 2.0;
}
