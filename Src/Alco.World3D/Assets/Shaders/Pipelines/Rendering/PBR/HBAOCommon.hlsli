// Shared per-frame data and helpers for the HBAO+ compute passes (HBAO.hlsl,
// HBAOBlur.hlsl). Include after Shaders/Libs/Core.hlsli. The cbuffer layout
// must match HbaoRenderer.HbaoData on the C# side exactly.

DEFINE_UNIFORM(0, _data)
{
    float4x4 invViewProjection;
    float4 cameraPosition; // xyz = world-space camera position
    float4 cameraRight;    // xyz = world-space camera right axis
    float4 cameraUp;       // xyz = world-space camera up axis
    float4 cameraForward;  // xyz = world-space camera forward axis
    float4 params;         // x = radius (world units), y = intensity exponent, z = angle bias (sin space), w = 1 / radius^2
    float4 params2;        // x = projScale (0.5 * viewportHeight * projection y-scale), yz = viewport size in pixels, w = max step length in pixels
    float4 params3;        // x = strength (multiplies the blurred AO into the G-buffer AO channel), yzw = unused
};

// Reconstruct the world-space position of a pixel from its UV and depth.
float3 ReconstructWorldPosition(float2 uv, float depth)
{
    float2 ndc = float2(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);
    float4 world = mul(invViewProjection, float4(ndc, depth, 1.0));
    return world.xyz / world.w;
}

// Depth along the camera forward axis (positive in front of the camera).
float ViewDepth(float3 worldPosition)
{
    return dot(worldPosition - cameraPosition.xyz, cameraForward.xyz);
}

// Interleaved gradient noise: stable per-pixel pseudo-random value, 0 (inclusive)
// to 1 (exclusive). Two decorrelated channels are obtained by re-hashing with an offset.
float InterleavedGradientNoise(uint2 pixel)
{
    return frac(52.9829189 * frac(dot(float2(pixel), float2(0.06711056, 0.00583715))));
}
