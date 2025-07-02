#include "Shaders/Libs/Core.hlsli"

struct Vertex {
  float3 position : POSITION;
  float2 uv : TEXCOORD0;
  uint instanceId : SV_InstanceID;
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

DEFINE_UNIFORM(0, _camera) { float4x4 viewProjection; };

DEFINE_TEX2D_SAMPLE(1, _texture);

DEFINE_STORAGE(2, Constants, _instances);

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

[shader("pixel")]
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