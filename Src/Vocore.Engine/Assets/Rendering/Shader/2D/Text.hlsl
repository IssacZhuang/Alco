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
  // the start of instance id in OpenGL is always 0, so use a custom instance start
  float2 vertexOffset; // to offset the text to pivot point
  uint instanceStart;
};

struct TextData {
  float4 uvRect;
  float4 color;
  float2 offset;
  float2 size;
};

DEFINE_STRUCT(0, _camera) { float4x4 viewProjection; };

DEFINE_STRUCT(1, _textBuffer) { TextData Data[MAX_INSTANCE_COUNT]; };

DEFINE_TEX2D_SAMPLE(2, _font);

PUSH_CONSTANT Constants constants;

V2F vs_main(Vertex2D input) {
  TextData data = Data[constants.instanceStart + input.instanceId];
  float2 vertexPos = input.position * data.size;
  float4 position =
      float4(vertexPos + data.offset + constants.vertexOffset, 0.0f, 1.0f);
  position = mul(constants.model, position);
  position = mul(viewProjection, position);

  V2F output = (V2F)0;
  output.position = position;
  output.uv = input.uv;
  output.instanceId = constants.instanceStart + input.instanceId;
  return output;
}

float4 fs_main(V2F input) : SV_TARGET {
  TextData data = Data[input.instanceId];
  float2 uv = input.uv * data.uvRect.zw + data.uvRect.xy;
  // float r = _font.Sample(_fontSampler, uv).r;
  float r = SAMPLE_TEX2D(_font, uv).r;
  return float4(r, r, r, r) * data.color;
}
