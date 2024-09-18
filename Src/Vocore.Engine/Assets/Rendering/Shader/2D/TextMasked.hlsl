#include "Rendering/ShaderLib/Core.hlslinc"

#pragma EntryVertex vs_main
#pragma EntryFragment fs_main

#pragma BlendState AlphaBlend
#pragma DepthStencilState Read


struct Vertex2D {
  float2 position : POSITION;
  float2 uv : TEXCOORD0;
  uint instanceId : SV_INSTANCEID;
};

struct V2F {
  float4 position : SV_POSITION;
  float2 uv : TEXCOORD0;
  float2 clipPos : TEXCOORD1;
  float2 maskMin : TEXCOORD2; // mask in clip space
  float2 maskMax : TEXCOORD3;
  uint instanceId : TEXCOORD4;
};

struct Constants {
  float4x4 model;
  float4 mask;//rect in world space
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

DEFINE_UNIFORM(0, _camera) { float4x4 viewProjection; };

DEFINE_STORAGE(1, TextData, _textBuffer);

DEFINE_TEX2D_SAMPLE(2, _font);

PUSH_CONSTANT Constants constants;

V2F vs_main(Vertex2D input) {
  TextData data = _textBuffer[constants.instanceStart + input.instanceId];
  float2 vertexPos = input.position * data.size;
  float4 position =
      float4(vertexPos + data.offset + constants.vertexOffset, 0.0f, 1.0f);
  position = mul(constants.model, position);
  position = mul(viewProjection, position);

  V2F output = (V2F)0;
  output.position = position;
  output.uv = input.uv;
  output.instanceId = constants.instanceStart + input.instanceId;

    //calc mask

  output.maskMin =
      mul(viewProjection, float4(constants.mask.xy, 0.0f, 1.0f)).xy;
  output.maskMax =
      mul(viewProjection, float4(constants.mask.zw, 0.0f, 1.0f)).xy;
  output.clipPos = position.xy;

  return output;
}

float4 fs_main(V2F input) : SV_TARGET {
    //discard if outside mask
  if (input.clipPos.x < input.maskMin.x || input.clipPos.x > input.maskMax.x ||
      input.clipPos.y < input.maskMin.y || input.clipPos.y > input.maskMax.y) {
    discard;
  }

  TextData data = _textBuffer[input.instanceId];
  float2 uv = input.uv * data.uvRect.zw + data.uvRect.xy;
  // float r = _font.Sample(_fontSampler, uv).r;
  float r = SAMPLE_TEX2D(_font, uv).r;
  return float4(r, r, r, r) * data.color;
}
