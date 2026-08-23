// file "sprite.slang" - module "sprite"
//
// Slang module converted from Shaders/Pipelines/Rendering/Sprite/Sprite.hlsl (Phase 2 migration).
// DEFINE_* macros became explicit set/binding declarations; bindings within a
// set are assigned in the original declaration order and are compiler-owned.

#language slang 2025
module sprite;

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
  float4 uvRect;
};

[[vk::binding(0, 0)]] cbuffer _camera { float4x4 viewProjection; };


[[vk::binding(0, 1)]] Texture2D _texture;
[[vk::binding(1, 1)]] SamplerState _textureSampler;

[[vk::push_constant]] Constants constants;

[shader("vertex")]
V2F MainVS(Vertex input) {
  V2F output = (V2F)0;
  output.position = mul(constants.model, float4(input.position, 1.0f));
  output.position = mul(viewProjection, output.position);
  output.uv = input.uv * constants.uvRect.zw + constants.uvRect.xy;
  return output;
}

[shader("fragment")]
float4 MainPS(V2F input) : SV_TARGET {
#if defined(REPEATED)
  float2 uv = frac(input.uv);
#else
  float2 uv = input.uv;
#endif
  float4 color = _texture.Sample(_textureSampler, uv) * constants.color;
  return color;
}