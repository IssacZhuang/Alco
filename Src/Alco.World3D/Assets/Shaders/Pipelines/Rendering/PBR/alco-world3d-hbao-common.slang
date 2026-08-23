// file "alco-world3d-hbao-common.slang" - module "alco_world3d_hbao_common"
//
// Slang module converted from Shaders/Pipelines/Rendering/PBR/HBAOCommon.hlsli (Phase 2 migration).
// DEFINE_* macros became explicit set/binding declarations; bindings within a
// set are assigned in the original declaration order and are compiler-owned.
// Lib declarations are public so importing modules see them.

#language slang 2025
module alco_world3d_hbao_common;

import alco_world3d_reversed_depth;

// Shared per-frame data and helpers for the HBAO+ compute passes (HBAO.hlsl,
// HBAOBlur.hlsl). Include after Shaders/Libs/Core.hlsli. The cbuffer layout
// must match HbaoRenderer.HbaoData on the C# side exactly.


public cbuffer _data : register(b0, space0) {
    public float4x4 invViewProjection;
    public float4 cameraPosition; // xyz = world-space camera position
    public float4 cameraRight;    // xyz = world-space camera right axis
    public float4 cameraUp;       // xyz = world-space camera up axis
    public float4 cameraForward;  // xyz = world-space camera forward axis
    public float4 params;         // x = radius (world units), y = intensity exponent, z = angle bias (sin space), w = 1 / radius^2
    public float4 params2;        // x = projScale (0.5 * viewportHeight * projection y-scale), yz = viewport size in pixels, w = max step length in pixels
    public float4 params3;        // x = strength (multiplies the blurred AO into the G-buffer AO channel), yzw = unused
};


// Reconstruct the world-space position of a pixel from its UV and depth.
// The blur pass reconstructs sky pixels without a sentinel check first, so the
// clamp keeps the homogeneous w finite under reversed infinite depth (the
// reconstructed sky point lands ~near / epsilon metres away, which the
// bilateral depth weight then rejects exactly like the old far plane).
public float3 ReconstructWorldPosition(float2 uv, float depth)
{
    float2 ndc = float2(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);
    float4 world = mul(invViewProjection, float4(ndc, max(depth, PBR_SKY_DEPTH_EPSILON), 1.0));
    return world.xyz / world.w;
}

// Depth along the camera forward axis (positive in front of the camera).
public float ViewDepth(float3 worldPosition)
{
    return dot(worldPosition - cameraPosition.xyz, cameraForward.xyz);
}

// Interleaved gradient noise: stable per-pixel pseudo-random value, 0 (inclusive)
// to 1 (exclusive). Two decorrelated channels are obtained by re-hashing with an offset.
public float InterleavedGradientNoise(uint2 pixel)
{
    return frac(52.9829189 * frac(dot(float2(pixel), float2(0.06711056, 0.00583715))));
}
