// file "sprite-instanced.slang" - module "sprite_instanced"
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
  uint instanceID : SV_VulkanInstanceID;
};

struct V2F {
  float4 position : SV_POSITION;
  float2 uv : TEXCOORD0;
  uint instanceID : TEXCOORD01;
};

struct Constants {
  float4x4 model;
  float4 color;
  float4 uvRect;
};

cbuffer _camera : register(b0, space0) { float4x4 viewProjection; };


cbuffer _material : register(b0, space1)
{
    Texture2D _texture;
    SamplerState _textureSampler;
};

cbuffer _instances : register(b0, space2)
{
    RWStructuredBuffer<Constants> _instances;
};

[shader("vertex")]
V2F MainVS(Vertex input) {
    V2F output = (V2F)0;
  Constants constants = _instances[input.instanceID];
  output.position = mul(constants.model, float4(input.position, 1.0f));
  output.position = mul(viewProjection, output.position);
  output.uv = input.uv * constants.uvRect.zw + constants.uvRect.xy;
  output.instanceID = input.instanceID;
  return output;
}

[shader("fragment")]
float4 MainPS(V2F input) : SV_TARGET {
  Constants constants = _instances[input.instanceID];
  float4 color = _texture.Sample(_textureSampler, input.uv) * constants.color;
  return color;
}