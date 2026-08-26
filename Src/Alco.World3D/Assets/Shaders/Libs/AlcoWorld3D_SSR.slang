// file "AlcoWorld3D_SSR.slang" - module "AlcoWorld3D_SSR"
//
// Explicit set/binding declarations; bindings within a set are assigned in
// declaration order and are compiler-owned.
// Lib declarations are public so importing modules see them.

#language slang 2025
module AlcoWorld3D_SSR;

import AlcoRendering_Core;

// Shared by the post-lighting SSR trace, temporal resolve and composite passes.
// The C# side writes members by name through UniformGraphicsBuffer (reflection-
// driven offsets), so this layout needs no CPU twin.
// Resources are grouped as ParameterBlocks — the compiler assigns each block
// its own descriptor set; members resolve by bare field name.
public struct SsrData
{
    public float4x4 ssrInvViewProjection;
    public float4x4 ssrViewProjection;
    public float4x4 ssrPreviousViewProjection;
    public float4 ssrCameraPosition;           // xyz = world-space camera position
    public float4 ssrPreviousCameraPosition;   // xyz = previous frame camera position
    public uint   fullWidth;                   // full-resolution scene width in pixels
    public uint   fullHeight;                  // full-resolution scene height in pixels
    public uint   traceWidth;                  // trace-resolution width in pixels
    public uint   traceHeight;                 // trace-resolution height in pixels
    public uint   frameIndex;                  // animation frame counter
    public bool   historyValid;                // temporal history may be reprojected
    public int    debugMode;                   // composite debug view (0 = off)
    public float  strength;                    // specular strength multiplier
    public float  maxTraceDistance;            // world units
    public float  roughnessCutoff;             // reflections fade out above this roughness
};

public ParameterBlock<SsrData> ssrData;


public struct Vertex {

    public float3 position : POSITION;
    public float2 uv : TEXCOORD0;

};

public struct V2F {

    public float4 position : SV_POSITION;
    public float2 uv : TEXCOORD0;

};

// Full-screen-quad vertex shader shared by the SSR post passes. A lib module
// owns no [shader(...)] entry points — each importing pipeline module declares
// its own MainVS entry forwarding to this.
public V2F ssrPostFullQuadVS(Vertex input)
{
    V2F output = (V2F)0;
    output.position = float4(input.position, 1.0);
    output.uv = input.uv;
    return output;
}

public float3 ssrPostReconstructWorldPosition(float2 uv, float depth)
{
    float2 ndc = float2(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);
    float4 world = mul(ssrData.ssrInvViewProjection, float4(ndc, depth, 1.0));
    return world.xyz / world.w;
}

public float2 ssrPostEncodeNormal(float3 normal)
{
    normal /= abs(normal.x) + abs(normal.y) + abs(normal.z);
    float2 encoded = normal.xy;
    if (normal.z < 0.0)
    {
        encoded = (1.0 - abs(encoded.yx))
            * float2(encoded.x >= 0.0 ? 1.0 : -1.0,
                     encoded.y >= 0.0 ? 1.0 : -1.0);
    }
    return encoded * 0.5 + 0.5;
}

public float3 ssrPostDecodeNormal(float2 encoded)
{
    float2 oct = encoded * 2.0 - 1.0;
    float3 normal = float3(oct, 1.0 - abs(oct.x) - abs(oct.y));
    if (normal.z < 0.0)
    {
        normal.xy = (1.0 - abs(normal.yx))
            * float2(normal.x >= 0.0 ? 1.0 : -1.0,
                     normal.y >= 0.0 ? 1.0 : -1.0);
    }
    return normalize(normal);
}

public bool ssrPostProjectWorldPosition(float3 worldPosition, out float3 screenPosition)
{
    float4 clip = mul(ssrData.ssrViewProjection, float4(worldPosition, 1.0));
    if (clip.w <= 0.0)
    {
        screenPosition = 0.0;
        return false;
    }

    float3 ndc = clip.xyz / clip.w;
    screenPosition = float3(ndc.x * 0.5 + 0.5, 0.5 - ndc.y * 0.5, ndc.z);
    return all(screenPosition.xy >= 0.0) && all(screenPosition.xy <= 1.0)
        && screenPosition.z >= 0.0 && screenPosition.z <= 1.0;
}

public uint ssrPostHashUint(uint value)
{
    value ^= value >> 16;
    value *= 0x7FEB352Du;
    value ^= value >> 15;
    value *= 0x846CA68Bu;
    value ^= value >> 16;
    return value;
}

public float ssrPostRandom(uint2 pixel, uint frameIndex, uint dimension)
{
    uint seed = pixel.x * 0x9E3779B9u;
    seed ^= pixel.y * 0x85EBCA6Bu;
    seed ^= frameIndex * 0xC2B2AE35u;
    seed ^= dimension * 0x27D4EB2Fu;
    return float(ssrPostHashUint(seed) >> 8) * (1.0 / 16777216.0);
}

public float3 ssrPostDecodeSRGB(float3 color)
{
    float3 lo = color / 12.92;
    float3 hi = pow(max((color + 0.055) / 1.055, 0.0), 2.4);
    return lerp(hi, lo, step(color, float3(0.04045, 0.04045, 0.04045)));
}

// Lazarov split-sum BRDF approximation (same formula as envBRDFApprox in
// AlcoWorld3D_PBRCommon).
public float3 ssrPostEnvBRDF(float3 f0, float roughness, float ndotV)
{
    const float4 c0 = float4(-1.0, -0.0275, -0.572, 0.022);
    const float4 c1 = float4(1.0, 0.0425, 1.04, -0.04);
    float4 r = roughness * c0 + c1;
    float a004 = min(r.x * r.x, exp2(-9.28 * ndotV)) * r.x + r.y;
    float2 ab = float2(-1.04, 1.04) * a004 + r.zw;
    return f0 * ab.x + ab.y;
}


