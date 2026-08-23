// file "hbao.slang" - module "hbao"
//
// Slang module converted from Shaders/Pipelines/Rendering/PBR/HBAO.hlsl (Phase 2 migration).
// DEFINE_* macros became explicit set/binding declarations; bindings within a
// set are assigned in the original declaration order and are compiler-owned.
// Lib declarations are public so importing modules see them.

#language slang 2025
module hbao;

import alco_rendering_core;
import alco_world3d_hbao_common;
import alco_world3d_reversed_depth;

// Transitional sampling shorthands: the preprocessor does not cross module
// boundaries, so the old Core.hlsli macros live here until Phase 3 deletes the
// name##Sampler convention outright.
#define SAMPLE_TEX2D(textureName, uv) textureName.Sample(textureName##Sampler, uv)
#define SAMPLE_TEX2D_LEVEL(textureName, uv, mip) textureName.SampleLevel(textureName##Sampler, uv, mip)
#define SAMPLE_TEX3D_LEVEL(textureName, uvw, mip) textureName.SampleLevel(textureName##Sampler, uvw, mip)
#define GET_PIXEL_TEX2D(textureName, position) textureName.Load(int3(position, 0))
#define LOAD_TEX3D(textureName, coord, mip) textureName.Load(int4(coord, mip))
#define SAMPLE_TEX2D_DEPTH_CMP(textureName, uv, compareDepth) textureName.SampleCmpLevelZero(textureName##Sampler, uv, compareDepth)

// HBAO+ (horizon-based ambient occlusion) raw pass for the deferred PBR pipeline.
// Marches screen-space rays in several per-pixel rotated directions, finds the
// highest occluder elevation (horizon) along each ray and integrates the
// unoccluded arc against the surface tangent plane (Bavoil & Sainz 2008, plus
// the NVIDIA HBAO+ refinements: per-pixel rotation/jitter noise, distance
// falloff and an angle bias). Writes raw (noisy) AO; HBAOBlur.hlsl filters it.

// Quality knobs as compile-time constants (nobody overrides them; the previous
// #ifndef default guards are gone with the preprocessor permutation cleanup).
static const int HBAO_NUM_DIRECTIONS = 6;
static const int HBAO_NUM_STEPS = 4;

[[vk::binding(0, 1)]] DepthTexture2D _gbufferDepth;
[[vk::binding(0, 2)]] Texture2D _normal;
[[vk::binding(0, 3)]] [[vk::image_format("rgba16f")]] RWTexture2D<float4> _aoOutput;

[shader("compute")]
[numthreads(8, 8, 1)]
void MainCS(uint3 dispatchId : SV_DispatchThreadID)
{
    uint2 pixel = dispatchId.xy;
    uint2 viewportSize = uint2(params2.y, params2.z);
    if (pixel.x >= viewportSize.x || pixel.y >= viewportSize.y)
    {
        return;
    }

    float depth = GetPixelDepth2D(_gbufferDepth, int2(pixel));
    if (IS_SKY_DEPTH(depth))
    {
        // Sky pixels are never occluded.
        _aoOutput[pixel] = float4(1.0, 1.0, 1.0, 1.0);
        return;
    }

    float2 uv = (float2(pixel) + 0.5) / float2(viewportSize);
    float3 position = ReconstructWorldPosition(uv, depth);
    float3 normal = normalize(GET_PIXEL_TEX2D(_normal, int2(pixel)).xyz * 2.0 - 1.0);
    float3 viewVector = normalize(cameraPosition.xyz - position);
    float viewDepth = ViewDepth(position);

    float radius = params.x;
    float invRadius2 = params.w;

    // Project the world-space radius to a per-direction step length in pixels at
    // this depth, clamped so rays neither degenerate nor march across the screen.
    float stepPixels = clamp(params2.x * radius / max(viewDepth, 1e-3) / HBAO_NUM_STEPS, 1.0, params2.w);
    float2 texelSize = 1.0 / float2(viewportSize);

    float rotationNoise = InterleavedGradientNoise(pixel);
    float stepJitter = InterleavedGradientNoise(pixel + uint2(113, 197));

    const float sliceAngle = TAU / HBAO_NUM_DIRECTIONS;
    float occlusionSum = 0.0;

    [unroll]
    for (int i = 0; i < HBAO_NUM_DIRECTIONS; i++)
    {
        float angle = (i + rotationNoise) * sliceAngle;
        float2 direction = float2(cos(angle), sin(angle));

        // World-space axis of this slice (screen UV y grows downward, opposite to
        // the camera up axis), orthogonalized against the view vector.
        float3 sliceAxis = direction.x * cameraRight.xyz - direction.y * cameraUp.xyz;
        sliceAxis = normalize(sliceAxis - viewVector * dot(sliceAxis, viewVector));

        // Elevation of the surface tangent inside the slice. With {u, V}
        // orthonormal the projected normal is (N.u, N.V) and the tangent is
        // perpendicular to it: sin(t) = -N.u / |N_p|.
        float2 normalSlice = float2(dot(normal, sliceAxis), dot(normal, viewVector));
        float sinTangent = -normalSlice.x / max(length(normalSlice), 1e-5);

        float directionOcclusion = 0.0;
        [unroll]
        for (int s = 0; s < HBAO_NUM_STEPS; s++)
        {
            float t = (s + 1.0 - stepJitter) / HBAO_NUM_STEPS;
            float2 sampleUV = uv + direction * stepPixels * t * texelSize;
            if (any(sampleUV < 0.0) || any(sampleUV > 1.0))
            {
                break;
            }

            float sampleDepth = GetPixelDepth2D(_gbufferDepth, int2(sampleUV * float2(viewportSize)));
            float3 samplePosition = ReconstructWorldPosition(sampleUV, sampleDepth);

            float3 delta = samplePosition - position;
            float distance2 = dot(delta, delta);
            // sin of the occluder elevation above the slice axis (delta.V measures
            // the elevation toward the camera); distant occluders fade out so the
            // radius has a soft boundary instead of a hard cutoff.
            float horizonSin = dot(delta, viewVector) * rsqrt(distance2 + 1e-8);
            float falloff = saturate(1.0 - distance2 * invRadius2);
            directionOcclusion = max(directionOcclusion, max(horizonSin - sinTangent - params.z, 0.0) * falloff);
        }
        occlusionSum += directionOcclusion;
    }

    float ao = saturate(1.0 - occlusionSum / HBAO_NUM_DIRECTIONS);
    ao = pow(ao, params.y);
    _aoOutput[pixel] = float4(ao, ao, ao, 1.0);
}
