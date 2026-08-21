#ifndef CHECKER_HLSLI
#define CHECKER_HLSLI

#include "Shaders/Libs/Core.hlsli"
#include "Shaders/Libs/Surface.hlsli"

// Example procedural surface: a world-space checkerboard with per-cell
// roughness variation. Declares no textures at all — materials bound to this
// surface need no texture streaming, and shaders compiled from it have no
// material texture slots. Pair with a material asset like:
//   { "version": "1.0", "shader": "Shaders/Materials/Checker.hlsli",
//     "parameters": { "checkerScale": 4.0 } }

// Surface parameters (the _materialParams convention of Surface.hlsli): one
// float4 register per member, filled from the asset's "parameters" object by
// name; unset members read zero.
DEFINE_UNIFORM(2, _materialParams)
{
    float4 checkerScale; // x = cells per meter; 0 = the default 2
};

// Identity vertex deformation: the checker does not animate vertices.
void ModifyVertex(inout float3 worldPos, inout float3 normalWS, float2 uv, float time)
{
}

SurfaceOutput EvaluateSurface(SurfaceInput input)
{
    SurfaceOutput output = (SurfaceOutput)0;

#if defined(PASS_SHADOW)
    // Shadow depth only needs alpha; the checker is always opaque.
    output.alpha = input.baseColorFactor.a;
    return output;
#else
    // cbuffer members are unqualified in HLSL (see Core.hlsli's DEFINE_UNIFORM).
    float cellsPerMeter = checkerScale.x > 0.0 ? checkerScale.x : 2.0;
    float3 cell = floor(input.worldPos * cellsPerMeter);
    float checker = fmod(cell.x + cell.y + cell.z, 2.0);

    float3 colorA = float3(0.85, 0.12, 0.10);
    float3 colorB = float3(0.90, 0.90, 0.92);
    output.albedo = lerp(colorB, colorA, checker) * input.baseColorFactor.rgb;
    output.alpha = input.baseColorFactor.a;
    output.normalTS = float3(0.0, 0.0, 1.0);
    output.roughness = lerp(0.55, 0.20, checker);
    output.metallic = 0.0;
    output.ao = 1.0;
    output.emissive = 0.0;

    return output;
#endif
}

#endif // CHECKER_HLSLI
