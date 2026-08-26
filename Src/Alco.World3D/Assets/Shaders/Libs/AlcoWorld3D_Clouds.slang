// file "AlcoWorld3D_Clouds.slang" - module "AlcoWorld3D_Clouds"
//
// Explicit set/binding declarations; bindings within a set are assigned in
// declaration order and are compiler-owned.
// Lib declarations are public so importing modules see them.

#language slang 2025
module AlcoWorld3D_Clouds;

import AlcoRendering_Core;

// Shared shaping helpers for the volumetric clouds (pure functions; the
// including shader samples the noise textures and owns every uniform).
// The technique follows the Nubis / Horizon Zero Dawn family: a low-frequency
// Perlin-Worley "base shape" reshaped by a coverage field and a height
// gradient, eroded at its silhouette by a high-frequency Worley "detail"
// octarve, lit by a short sun march with Beer-Powder extinction and a
// dual-lobe Henyey-Greenstein phase.
//
// The noise textures are generated once by VolumetricCloudNoise.slang:
//   base (128^3 RGBA8): R = Perlin-Worley, G/B/A = Worley FBM octaves
//   detail (32^3 RGBA8): R/G/B = Worley FBM octaves (A unused)

// Remap v from [l0, l1] onto [r0, r1]. Unclamped by design so callers choose
// where to saturate; the epsilon keeps a closed window from dividing by zero.
public float cloudRemap(float v, float l0, float l1, float r0, float r1)
{
    return r0 + (v - l0) * (r1 - r0) / max(l1 - l0, 1e-6);
}

// Henyey-Greenstein phase (same formulation as VolumetricLight.slang).
public float cloudPhaseHG(float cosTheta, float g)
{
    float g2 = g * g;
    float denom = 1.0 + g2 - 2.0 * g * cosTheta;
    return (1.0 - g2) / (4.0 * PI * max(pow(denom, 1.5), 1e-7));
}

// Dual-lobe phase: a strong forward peak (silver lining toward the sun) mixed
// with a mild back-scatter lobe (soft grey when looking away). Mixing ~0.35 of
// the back lobe keeps the anti-solar side from going flat black.
public float cloudPhaseDual(float cosTheta)
{
    float forward = cloudPhaseHG(cosTheta, 0.72);
    float back = cloudPhaseHG(cosTheta, -0.18);
    return lerp(forward, back, 0.35);
}

// Height gradient inside the cloud slab (0 at the faces, 1 mid-slab). The top
// descent starts higher as coverage grows, so dense weather builds tall
// rounded towers while sparse skies stay thin and wispy.
public float cloudHeightGradient(float h, float coverage)
{
    float rise = saturate(cloudRemap(h, 0.0, 0.07 + 0.10 * (1.0 - coverage), 0.0, 1.0));
    float fall = saturate(cloudRemap(h, 0.45 + 0.35 * coverage, 1.0, 1.0, 0.0));
    return rise * fall;
}

// Coverage mask from the low-frequency field: the coverage parameter slides
// the empty-sky → overcast window over the same field, so raising coverage
// grows existing clouds first and only then closes the gaps.
public float cloudCoverageMask(float lowFreq, float coverage)
{
    return saturate(cloudRemap(lowFreq, 1.0 - coverage, 0.98, 0.0, 1.0));
}

// FBM weights combining the base texture's Worley octaves / the detail
// texture's octaves (the classic 0.625 / 0.25 / 0.125 distribution).
public float cloudWorleyFbm(float4 baseNoise)
{
    return baseNoise.g * 0.625 + baseNoise.b * 0.25 + baseNoise.a * 0.125;
}

public float cloudDetailFbm(float3 detailNoise)
{
    return detailNoise.r * 0.625 + detailNoise.g * 0.25 + detailNoise.b * 0.125;
}

// Carve the Perlin-Worley channel by its Worley octaves into the billowy base
// shape, then reshape by coverage mask and height gradient.
public float cloudBaseShape(float4 baseNoise, float coverageMask, float heightGradient)
{
    float carved = saturate(cloudRemap(baseNoise.r, cloudWorleyFbm(baseNoise) - 1.0, 1.0, 0.0, 1.0));
    return saturate(carved * coverageMask * heightGradient);
}

// Erode the base shape's silhouette with the high-frequency detail octaves.
public float cloudErodeShape(float density, float3 detailNoise, float detailStrength, float heightFraction)
{
    // Erode harder near the slab bottom (wispy evaporating bases) and let the
    // detail fade toward the tops so summits keep their rounded density.
    float verticalBias = lerp(1.0, 0.55, saturate(heightFraction * 1.4));
    float erode = cloudDetailFbm(detailNoise) * detailStrength * verticalBias;
    return saturate(cloudRemap(density, erode, 1.0, 0.0, 1.0));
}


