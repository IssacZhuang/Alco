// file "reinhard-luminance-tonemap.slang" - module "reinhard_luminance_tonemap"
//
// Slang module converted from Shaders/Pipelines/PostProcess/Tonemap/ReinhardLuminanceTonemap.hlsl (Phase 2 migration).
// DEFINE_* macros became explicit set/binding declarations; bindings within a
// set are assigned in the original declaration order and are compiler-owned.

#language slang 2025
module reinhard_luminance_tonemap;

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

cbuffer _pass : register(b0, space0)
{
    Texture2D _texture;
    SamplerState _textureSampler; // should be HDR image
};
cbuffer _data : register(b0, space1) {
    float MaxLuminance;
    float Gamma;
};


struct Vertex {
  float3 position : POSITION;
  float2 uv : TEXCOORD0;
};

struct V2F {
  float4 position : SV_POSITION;
  float2 uv : TEXCOORD0;
};

[shader("vertex")]
V2F MainVS(Vertex input) {
  V2F output = (V2F)0;
  output.position = float4(input.position, 1.0f);
  output.uv = input.uv;
  return output;
}

float luminance(float3 color) {
  return dot(color, float3(0.2126, 0.7152, 0.0722));
}

float3 change_luminance(float3 color, float new_luminance) {
  return color * (new_luminance / luminance(color));
}

[shader("fragment")]
float4 MainPS(V2F input) : SV_TARGET {
  float4 source = SAMPLE_TEX2D(_texture, input.uv);

  float old_luminance = dot(source.rgb, float3(0.2126, 0.7152, 0.0722));
  float numerator = old_luminance * (1.0 + (old_luminance/(MaxLuminance*MaxLuminance)));
  float new_luminance = numerator / (1.0 + old_luminance);

  float3 ldrColor = change_luminance(source.rgb, new_luminance);
  ldrColor = pow(ldrColor, 1.0 / Gamma);
  ldrColor += OutputDither8Bit(input.position.xy);
  return float4(ldrColor, 1.0);
}