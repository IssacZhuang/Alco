#include "Rendering/ShaderLib/Core.hlslinc"

#pragma EntryVertex vs_main
#pragma EntryFragment fs_main

#pragma BlendState AlphaBlend
#pragma DepthStencilState Read

#define MAX_INSTANCE_COUNT 300


struct Vertex2D {
  float2 position : POSITION;
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

V2F vs_main(Vertex2D input) {
  TextData data = _textBuffer[input.instanceId];
  float2 vertexPos = input.position * data.size;
  float4 position =
      float4(vertexPos + data.offset + constants.vertexOffset, 0.0f, 1.0f);
  position = mul(constants.model, position);
  position = mul(viewProjection, position);

  V2F output = (V2F)0;
  output.position = position;
  output.uv = input.uv;
  output.instanceId = input.instanceId;
  return output;
}

float4 fs_main(V2F input) : SV_TARGET {
  TextData data = _textBuffer[input.instanceId];
  float2 uv = input.uv * data.uvRect.zw + data.uvRect.xy;
  // float r = _font.Sample(_fontSampler, uv).r;
  float r = SAMPLE_TEX2D(_font, uv).r;
  return float4(r, r, r, r) * data.color;
}
