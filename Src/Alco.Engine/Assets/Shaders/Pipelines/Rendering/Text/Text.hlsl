#include "Shaders/Libs/Core.hlsli"

#define MAX_INSTANCE_COUNT 300


struct Vertex {
  float3 position : POSITION;
  float2 uv : TEXCOORD0;
  uint instanceId : SV_INSTANCEID;
};

struct V2F {
  float4 position : SV_POSITION;
  float2 uv : TEXCOORD0;
  uint instanceId : TEXCOORD1;
};

struct Constants {
  float4x4 model;
  float2 vertexOffset; // to offset the text to pivot point
};

struct TextData {
  float4 uvRect;
  float4 color;
  float2 offset;
  float2 size;
};

DEFINE_UNIFORM(0, _camera) { float4x4 viewProjection; };

DEFINE_STORAGE(1, TextData, _textBuffer);

DEFINE_TEX2D_SAMPLE(2, _font);

PUSH_CONSTANT Constants constants;

[shader("vertex")]
V2F MainVS(Vertex input) {
  TextData data = _textBuffer[input.instanceId];
  float3 vertexPos = input.position * float3(data.size, 1.0f);
  float3 offset = float3(data.offset + constants.vertexOffset, 0.0f);
  float4 position = float4(vertexPos + offset, 1.0f);
  position = mul(constants.model, position);
  position = mul(viewProjection, position);

  V2F output = (V2F)0;
  output.position = position;
  output.uv = input.uv;
  output.instanceId = input.instanceId;
  return output;
}

[shader("pixel")]
float4 MainPS(V2F input) : SV_TARGET {
  TextData data = _textBuffer[input.instanceId];
  float2 uv = input.uv * data.uvRect.zw + data.uvRect.xy;
  // float r = _font.Sample(_fontSampler, uv).r;
  float r = SAMPLE_TEX2D(_font, uv).r;
  // Apply gamma correction (sRGB to linear)
  r = pow(r, 1/2.2);
#if defined(ALPHA_TEST)
  if (r < 0.01f)
  {
    discard;
  }
#endif
  return float4(r, r, r, r) * data.color;
}
