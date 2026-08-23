// file "inverser-gamma.slang" - module "inverser_gamma"
//
// Slang module converted from Assets/InverserGamma.hlsl (Phase 2 migration).
// DEFINE_* macros became explicit set/binding declarations.

#language slang 2025
module inverser_gamma;

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
  output.uv = input.uv * constants.uvRect.zw + constants.uvRect.xy;
  return output;
}

[shader("fragment")]
float4 MainPS(V2F input) : SV_TARGET {
  float4 color = SAMPLE_TEX2D(_texture, input.uv) * constants.color;
  //inverse gamma
  color.rgb = pow(color.rgb, 2.2f);
  return color;
}
