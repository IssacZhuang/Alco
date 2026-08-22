// file "FilmicTonemap.slang" - module "filmic_tonemap"
//
// Slang module converted from Shaders/Pipelines/PostProcess/Tonemap/FilmicTonemap.hlsl (Phase 2 migration).
// DEFINE_* macros became explicit set/binding declarations; bindings within a
// set are assigned in the original declaration order and are compiler-owned.

#language slang 2025
module filmic_tonemap;

import alco_rendering_core;

// Transitional sampling shorthands: the preprocessor does not cross module
// boundaries, so the old Core.hlsli macros live here until Phase 3 deletes the
// name##Sampler convention outright.
#define SAMPLE_TEX2D(textureName, uv) textureName.Sample(textureName##Sampler, uv)
#define SAMPLE_TEX2D_LEVEL(textureName, uv, mip) textureName.SampleLevel(textureName##Sampler, uv, mip)
#define SAMPLE_TEX3D_LEVEL(textureName, uvw, mip) textureName.SampleLevel(textureName##Sampler, uvw, mip)
#define GET_PIXEL_TEX2D(textureName, position) textureName.Load(int3(position, 0))
#define LOAD_TEX3D(textureName, coord, mip) textureName.Load(int4(coord, mip))
#define SAMPLE_TEX2D_DEPTH_CMP(textureName, uv, compareDepth) textureName.SampleCmpLevelZero(textureName##Sampler, uv, compareDepth)

[[vk::binding(0, 0)]] Texture2D _texture;
[[vk::binding(1, 0)]] SamplerState _textureSampler;
[[vk::binding(0, 1)]] cbuffer _data {
    float Exposure;
    float Gamma;
};


struct Vertex { float3 position : POSITION; float2 uv : TEXCOORD0; };
struct V2F { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };

float3 Filmic(float3 x) {
  float3 X = max(float3(0,0,0), x - 0.004);
  float3 result = (X * (6.2 * X + 0.5)) / (X * (6.2 * X + 1.7) + 0.06);
  return result;
}

[shader("vertex")]
V2F MainVS(Vertex input){ V2F o=(V2F)0; o.position=float4(input.position,1); o.uv=input.uv; return o; }

[shader("fragment")]
float4 MainPS(V2F input): SV_TARGET
{
    float3 v = SAMPLE_TEX2D(_texture, input.uv).rgb * Exposure;
    float3 ldr = Filmic(v);
    ldr = pow(ldr, 1.0 / Gamma);
    ldr += OutputDither8Bit(input.position.xy);
    return float4(ldr, 1.0);
}

