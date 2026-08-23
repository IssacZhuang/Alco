// file "tile-connectable.slang" - module "tile_connectable"
//
// Slang module converted from Assets/Shaders/TileConnectable.hlsl (Phase 2
// migration). DEFINE_* macros became explicit set/binding declarations.

#language slang 2025
module tile_connectable;

// Transitional sampling shorthands: the preprocessor does not cross module
// boundaries, so the old Core.hlsli macros live here until Phase 3 deletes the
// name##Sampler convention outright.
#define SAMPLE_TEX2D(textureName, uv) textureName.Sample(textureName##Sampler, uv)

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
  float2 size;
  float2 offset;
};

[[vk::binding(0, 0)]] cbuffer _camera { float4x4 viewProjection; };

[[vk::binding(0, 1)]] Texture2D _texture;
[[vk::binding(1, 1)]] SamplerState _textureSampler;

[[vk::push_constant]] Constants constants;

[shader("vertex")]
V2F MainVS(Vertex input) {
  V2F output = (V2F)0;

  float3 pos = input.position;
  //make it render as a facade
  pos.xy *= constants.size;
  pos.z = 0.5f - pos.y;
  pos.xy += constants.offset;
  output.position = mul(constants.model, float4(pos, 1.0f));
  output.position = mul(viewProjection, output.position);
  output.uv = input.uv * constants.uvRect.zw + constants.uvRect.xy;
  return output;
}

[shader("fragment")]
float4 MainPS(V2F input) : SV_TARGET {
  float4 color = SAMPLE_TEX2D(_texture, input.uv) * constants.color;
#if defined(ALPHA_TEST)
  if (color.a < 0.01f)
  {
    discard;
  }
#endif
  return color;
}
