#include "Rendering/ShaderLib/Core.hlslinc"

#pragma EntryVertex vs_main
#pragma EntryFragment fs_main

#pragma BlendState AlphaBlend
#pragma DepthStencilState Read

struct Vertex2D {
  float2 position : POSITION;
  float2 uv : TEXCOORD0;
};

struct V2F {
  float4 position : SV_POSITION;
  float2 uv : TEXCOORD0;
  float2 maskMin : TEXCOORD1; // mask in clip space
  float2 maskMax : TEXCOORD2;
};

struct Constants {
  float4x4 model;
  float4 mask;//rect in world space
  float4 color;
  float4 uvRect;
};

DEFINE_STRUCT(0, _camera) { float4x4 viewProjection; };

DEFINE_TEX2D_SAMPLE(1, _texture);

PUSH_CONSTANT Constants constants;

V2F vs_main(Vertex2D input) {
  V2F output = (V2F)0;
  output.position = mul(constants.model, float4(input.position, 0.0f, 1.0f));
  output.position = mul(viewProjection, output.position);
  output.uv = input.uv * constants.uvRect.zw + constants.uvRect.xy;

    //calc mask
    output.maskMin = mul(viewProjection, float4(constants.mask.xy, 0.0f, 1.0f));
    output.maskMax = mul(viewProjection, float4(constants.mask.zw, 0.0f, 1.0f));

  return output;
}

float4 fs_main(V2F input) : SV_TARGET {
    //discard if outside mask
    if (input.position.x < input.maskMin.x || input.position.x > input.maskMax.x || input.position.y < input.maskMin.y || input.position.y > input.maskMax.y)
    {
        discard;
    }
  return _texture.Sample(_textureSampler, input.uv) * constants.color;
}