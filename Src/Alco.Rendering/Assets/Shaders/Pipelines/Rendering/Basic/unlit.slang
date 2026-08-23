// file "unlit.slang" - module "unlit"
//
// Slang module converted from Shaders/Pipelines/Rendering/Basic/Unlit.hlsl (Phase 2 migration).
// DEFINE_* macros became explicit set/binding declarations; bindings within a
// set are assigned in the original declaration order and are compiler-owned.

#language slang 2025
module unlit;

import alco_rendering_core;

struct Vertex {
  float3 position : POSITION;
  float2 uv : TEXCOORD0;
};

struct V2F {
  float4 position : SV_POSITION;
  float2 uv : TEXCOORD0;
};

struct Constants {
  float4x4 model;
  float4 color;
};

cbuffer _camera : register(b0, space0) { float4x4 viewProjection; };


cbuffer _material : register(b0, space1)
{
    Texture2D _texture;
    SamplerState _textureSampler;
};

[[vk::push_constant]] Constants constants;

[shader("vertex")]
V2F MainVS(Vertex input) {
  V2F output = (V2F)0;
  output.position = mul(constants.model, float4(input.position, 1.0f));
  output.position = mul(viewProjection, output.position);
  output.uv = input.uv;
  return output;
}

[shader("fragment")]
float4 MainPS(V2F input) : SV_TARGET {
  return _texture.Sample(_textureSampler, input.uv) * constants.color;
}