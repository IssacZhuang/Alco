// file "VolumetricCloudNoise.slang" - module "VolumetricCloudNoise"
//
// Slang module converted from Shaders/Pipelines/Rendering/PBR/VolumetricCloudNoise.hlsl (Phase 2 migration).
// Resources are grouped as ParameterBlocks — the compiler assigns each block
// its own descriptor set in declaration order; members resolve by bare field name.
// Lib declarations are public so importing modules see them.

#language slang 2025
module VolumetricCloudNoise;

import AlcoRendering_Core;

// One-shot compute bake of the volumetric cloud noise textures (dispatched by
// RGNode_VolumetricClouds at startup; never runs per frame). Everything is
// tileable so the noise repeats seamlessly across the sky:
//   NOISE_BASE  — 128^3 RGBA8. R = Perlin-Worley (Perlin FBM reshaped by a
//                 Worley FBM at the same frequency), G/B/A = Worley FBM at
//                 2x/4x/8x the base frequency. Sampled at ~24 km world period
//                 for the cloud base shape.
//   NOISE_DETAIL — 32^3 RGBA8. R/G/B = Worley FBM at increasing frequency,
//                 sampled at ~2 km world period to erode cloud silhouettes.
// Storage is unorm rgba8 ("rgba8" in DXC image_format spelling).

float3 cloudNoiseHash33(float3 p)
{
    p = frac(p * float3(0.1031, 0.1030, 0.0973));
    p += dot(p, p.yxz + 33.33);
    return frac((p.xxy + p.yxx) * p.zyx);
}

// Worley cellular noise, tileable at the given integer frequency. Returns
// 1 - (distance to the nearest feature point), roughly [0, 1].
float cloudNoiseWorley(float3 uvw, int frequency)
{
    float3 p = uvw * (float)frequency;
    float3 id = floor(p);
    float3 f = frac(p);
    float minDist2 = 3.0;
    [unroll]
    for (int z = -1; z <= 1; z++)
    {
        [unroll]
        for (int y = -1; y <= 1; y++)
        {
            [unroll]
            for (int x = -1; x <= 1; x++)
            {
                float3 cell = float3(x, y, z);
                // Wrap the cell id modulo the frequency so the pattern tiles.
                float3 wrapped = (id + cell + frequency) % frequency;
                float3 feature = cloudNoiseHash33(wrapped);
                float3 diff = cell + feature - f;
                minDist2 = min(minDist2, dot(diff, diff));
            }
        }
    }
    return 1.0 - sqrt(minDist2);
}

// Tileable Perlin gradient noise, roughly [-1, 1].
float cloudNoisePerlin(float3 uvw, int frequency)
{
    float3 p = uvw * (float)frequency;
    float3 id = floor(p);
    float3 f = frac(p);
    float3 u = f * f * (3.0 - 2.0 * f);

    float sum = 0.0;
    [unroll]
    for (int z = 0; z <= 1; z++)
    {
        [unroll]
        for (int y = 0; y <= 1; y++)
        {
            [unroll]
            for (int x = 0; x <= 1; x++)
            {
                float3 corner = float3(x, y, z);
                float3 wrapped = (id + corner + frequency) % frequency;
                float3 gradient = cloudNoiseHash33(wrapped) * 2.0 - 1.0;
                sum += dot(gradient, f - corner)
                    * (z == 0 ? (1.0 - u.z) : u.z)
                    * (y == 0 ? (1.0 - u.y) : u.y)
                    * (x == 0 ? (1.0 - u.x) : u.x);
            }
        }
    }
    return sum * 1.6; // rough normalization toward [-1, 1]
}

float cloudNoisePerlinFbm(float3 uvw, int frequency, int octaves)
{
    float sum = 0.0;
    float amplitude = 0.5;
    [loop]
    for (int o = 0; o < octaves; o++)
    {
        sum += amplitude * cloudNoisePerlin(uvw, frequency);
        frequency *= 2;
        amplitude *= 0.5;
    }
    return saturate(0.5 + sum);
}

float cloudNoiseWorleyFbm(float3 uvw, int frequency, int octaves)
{
    float sum = 0.0;
    float amplitude = 0.5;
    [loop]
    for (int o = 0; o < octaves; o++)
    {
        sum += amplitude * cloudNoiseWorley(uvw, frequency);
        frequency *= 2;
        amplitude *= 0.5;
    }
    return sum;
}

float cloudRemapBake(float v, float l0, float l1, float r0, float r1)
{
    return r0 + (v - l0) * (r1 - r0) / max(l1 - l0, 1e-6);
}

[[vk::image_format("rgba8")]] RWTexture3D<float4> noiseOut;

// The bake kind is a generic value specialization (plan D3): true = detail
// bake (32^3 Worley FBM), false = base shape bake (128^3 Perlin-Worley). The
// C# owner requests the specialized entry (mainCS<true> / mainCS<false>)
// instead of passing a preprocessor permutation.
[shader("compute")]
[numthreads(4, 4, 4)]
void mainCS<let isDetail : bool>(uint3 dispatchId : SV_DispatchThreadID)
{
    if (isDetail)
    {
        const float size = 32.0;
        float3 uvw = (dispatchId + 0.5) / size;
        float3 octaves = float3(
            cloudNoiseWorleyFbm(uvw, 2, 2),
            cloudNoiseWorleyFbm(uvw, 4, 2),
            cloudNoiseWorleyFbm(uvw, 8, 2));
        noiseOut[dispatchId] = float4(octaves, octaves.z);
    }
    else // base shape bake
    {
        const float size = 128.0;
        float3 uvw = (dispatchId + 0.5) / size;
        float perlin = cloudNoisePerlinFbm(uvw, 4, 4);
        float worley = cloudNoiseWorleyFbm(uvw, 4, 3);
        // Perlin-Worley: carve the Perlin FBM by the Worley FBM so the resulting
        // shape has both large billows and cauliflower detail (Nubis R channel).
        float perlinWorley = saturate(cloudRemapBake(perlin, worley, 1.0, 0.0, 1.0));
        noiseOut[dispatchId] = float4(
            perlinWorley,
            cloudNoiseWorleyFbm(uvw, 8, 2),
            cloudNoiseWorleyFbm(uvw, 16, 2),
            cloudNoiseWorleyFbm(uvw, 32, 1));
    }
}
