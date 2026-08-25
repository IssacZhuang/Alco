// file "hbao.slang" - module "hbao"
//
// Slang module converted from Shaders/Pipelines/Rendering/PBR/HBAO.hlsl (Phase 2 migration).
// Resources are grouped as ParameterBlocks — the compiler assigns each block
// its own descriptor set in declaration order; members resolve by bare field name.
// Lib declarations are public so importing modules see them.

#language slang 2025
module hbao;

import alco_rendering_core;
import alco_world3d_hbao_common;
import alco_world3d_reversed_depth;

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

// alco_world3d_hbao_common declares _data; in the composed program the root
// module's own blocks come first, so _pass takes set 0 and _data follows.
public struct PassParams
{
    public DepthTexture2D _gbufferDepth;
    public Texture2D _normal;
    public [[vk::image_format("rgba16f")]] RWTexture2D<float4> _aoOutput;
};

public ParameterBlock<PassParams> _pass;


[shader("compute")]
[numthreads(8, 8, 1)]
void MainCS(uint3 dispatchId : SV_DispatchThreadID)
{
    uint2 pixel = dispatchId.xy;
    uint2 viewportSize = uint2(_data.params2.y, _data.params2.z);
    if (pixel.x >= viewportSize.x || pixel.y >= viewportSize.y)
    {
        return;
    }

    float depth = GetPixelDepth2D(_pass._gbufferDepth, int2(pixel));
    if (IS_SKY_DEPTH(depth))
    {
        // Sky pixels are never occluded.
        _pass._aoOutput[pixel] = float4(1.0, 1.0, 1.0, 1.0);
        return;
    }

    float2 uv = (float2(pixel) + 0.5) / float2(viewportSize);
    float3 position = ReconstructWorldPosition(uv, depth);
    float3 normal = normalize(_pass._normal.Load(int3(int2(pixel), 0)).xyz * 2.0 - 1.0);
    float3 viewVector = normalize(_data.cameraPosition.xyz - position);
    float viewDepth = ViewDepth(position);

    float radius = _data.params.x;
    float invRadius2 = _data.params.w;

    // Project the world-space radius to a per-direction step length in pixels at
    // this depth, clamped so rays neither degenerate nor march across the screen.
    float stepPixels = clamp(_data.params2.x * radius / max(viewDepth, 1e-3) / HBAO_NUM_STEPS, 1.0, _data.params2.w);
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
        float3 sliceAxis = direction.x * _data.cameraRight.xyz - direction.y * _data.cameraUp.xyz;
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

            float sampleDepth = GetPixelDepth2D(_pass._gbufferDepth, int2(sampleUV * float2(viewportSize)));
            float3 samplePosition = ReconstructWorldPosition(sampleUV, sampleDepth);

            float3 delta = samplePosition - position;
            float distance2 = dot(delta, delta);
            // sin of the occluder elevation above the slice axis (delta.V measures
            // the elevation toward the camera); distant occluders fade out so the
            // radius has a soft boundary instead of a hard cutoff.
            float horizonSin = dot(delta, viewVector) * rsqrt(distance2 + 1e-8);
            float falloff = saturate(1.0 - distance2 * invRadius2);
            directionOcclusion = max(directionOcclusion, max(horizonSin - sinTangent - _data.params.z, 0.0) * falloff);
        }
        occlusionSum += directionOcclusion;
    }

    float ao = saturate(1.0 - occlusionSum / HBAO_NUM_DIRECTIONS);
    ao = pow(ao, _data.params.y);
    _pass._aoOutput[pixel] = float4(ao, ao, ao, 1.0);
}
