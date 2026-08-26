// file "AlcoWorld3D_HBAOCommon.slang" - module "AlcoWorld3D_HBAOCommon"
//
// Explicit set/binding declarations; bindings within a set are assigned in
// declaration order and are compiler-owned.
// Lib declarations are public so importing modules see them.

#language slang 2025
module AlcoWorld3D_HBAOCommon;

import AlcoWorld3D_ReversedDepth;

// Shared per-frame data and helpers for the HBAO+ compute passes (hbao,
// hbao-blur). The C# side writes members by name through UniformGraphicsBuffer
// (reflection-driven offsets), so this layout needs no CPU twin. Resources are
// grouped as ParameterBlocks — the compiler assigns each block its own
// descriptor set; members resolve by bare name.

public struct HbaoData
{
    public float4x4 invViewProjection;
    public float4 cameraPosition; // xyz = world-space camera position
    public float4 cameraRight;    // xyz = world-space camera right axis
    public float4 cameraUp;       // xyz = world-space camera up axis
    public float4 cameraForward;  // xyz = world-space camera forward axis
    public float  radius;         // world units
    public float  intensity;      // exponent applied to the occlusion term
    public float  bias;           // angle bias (sin space)
    public float  invRadius2;     // 1 / radius^2
    public float  projScale;      // 0.5 * viewportHeight * projection y-scale
    public uint   viewportWidth;  // viewport size in pixels
    public uint   viewportHeight;
    public float  maxStepPixels;  // max step length in pixels
    public float  strength;       // multiplies the blurred AO into the G-buffer AO channel
};

public ParameterBlock<HbaoData> data;


// Reconstruct the world-space position of a pixel from its UV and depth.
// The blur pass reconstructs sky pixels without a sentinel check first, so the
// clamp keeps the homogeneous w finite under reversed infinite depth; the
// reconstructed far point is then rejected by the bilateral depth weight.
public float3 reconstructWorldPosition(float2 uv, float depth)
{
    float2 ndc = float2(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);
    float4 world = mul(data.invViewProjection, float4(ndc, max(depth, PBR_SKY_DEPTH_EPSILON), 1.0));
    return world.xyz / world.w;
}

// Depth along the camera forward axis (positive in front of the camera).
public float viewDepth(float3 worldPosition)
{
    return dot(worldPosition - data.cameraPosition.xyz, data.cameraForward.xyz);
}

// Interleaved gradient noise: stable per-pixel pseudo-random value, 0 (inclusive)
// to 1 (exclusive). Two decorrelated channels are obtained by re-hashing with an offset.
public float interleavedGradientNoise(uint2 pixel)
{
    return frac(52.9829189 * frac(dot(float2(pixel), float2(0.06711056, 0.00583715))));
}
