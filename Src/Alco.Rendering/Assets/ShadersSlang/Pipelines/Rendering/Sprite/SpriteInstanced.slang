// file "SpriteInstanced.slang" - module "sprite_instanced"
//
// Slang module converted from Shaders/Pipelines/Rendering/Sprite/SpriteInstanced.hlsl (Phase 2 migration).
// DEFINE_* macros became explicit set/binding declarations; bindings within a
// set are assigned in the original declaration order and are compiler-owned.

#language slang 2025
module sprite_instanced;

import alco_rendering_core;

struct Vertex {
  float3 position : POSITION;
  float2 uv : TEXCOORD0;
  uint instanceId : SV_VulkanInstanceID;
};

struct V2F {
  float4 position : SV_POSITION;
  float2 uv : TEXCOORD0;
  uint instanceId : TEXCOORD01;
};

struct Constants {
  float4x4 model;
  float4 color;
  float4 uvRect;
};

[[vk::binding(0, 0)]] cbuffer _camera { float4x4 viewProjection; };


[[vk::binding(0, 1)]] Texture2D _texture;
[[vk::binding(1, 1)]] SamplerState _textureSampler;

[[vk::binding(0, 2)]] RWStructuredBuffer<Constants> _instances;

[shader("vertex")]
V2F MainVS(Vertex input) {
    V2F output = (V2F)0;
  Constants constants = _instances[input.instanceId];
  output.position = mul(constants.model, float4(input.position, 1.0f));
  output.position = mul(viewProjection, output.position);
  output.uv = input.uv * constants.uvRect.zw + constants.uvRect.xy;
  output.instanceId = input.instanceId;
  return output;
}

[shader("fragment")]
float4 MainPS(V2F input) : SV_TARGET {
  Constants constants = _instances[input.instanceId];
  float4 color = _texture.Sample(_textureSampler, input.uv) * constants.color;
#if defined(ALPHA_TEST)
  if (color.a < 0.01f)
  {
    discard;
  }
#endif  
  return color;
}