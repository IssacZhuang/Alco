#include "Shaders/Libs/Core.hlsli"

struct Vertex {
  float3 position : POSITION;
  float2 uv : TEXCOORD0;
  uint instanceId : SV_INSTANCEID;
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

DEFINE_UNIFORM(0, _camera) { float4x4 viewProjection; };

DEFINE_TEX2D_SAMPLE(1, _texture);

DEFINE_STORAGE(2, float4, _offsetData);

PUSH_CONSTANT Constants constants;

[shader("vertex")]
V2F MainVS(Vertex input) {
  V2F output = (V2F)0;
  float4 offset = _offsetData[input.instanceId];
  float4 position = float4(input.position, 1.0f);
  position.xyz += offset.xyz;
  output.position = mul(constants.model, position);
  output.position = mul(viewProjection, output.position);
  output.uv = input.uv * constants.uvRect.zw + constants.uvRect.xy;
  return output;
}

[shader("pixel")]
float4 MainPS(V2F input) : SV_TARGET {
  float4 color = _texture.Sample(_textureSampler, input.uv) * constants.color;
#if defined(ALPHA_TEST)
  if (color.a < 0.01f)
  {
    discard;
  }
#endif  
  return color;
}