#include "Shaders/Libs/Core.hlsli"

struct Vertex {
    float3 position : POSITION;
    float2 uv : TEXCOORD0;
};

struct V2F {
  float4 position : SV_POSITION;
  float2 uv : TEXCOORD0;
  float2 clipPos : TEXCOORD1;
  float2 maskMin : TEXCOORD2; // mask in clip space
  float2 maskMax : TEXCOORD3;
};

struct Constants {
  float4x4 model;
  float4 mask;//rect in world space
  float4 color;
  float4 uvRect;
};

DEFINE_UNIFORM(0, _camera) { float4x4 viewProjection; };

DEFINE_TEX2D_SAMPLE(1, _texture);

PUSH_CONSTANT Constants constants;

[shader("vertex")]
V2F MainVS(Vertex input) {
  V2F output = (V2F)0;
  output.position = mul(constants.model, float4(input.position, 1.0f));
  output.position = mul(viewProjection, output.position);
  output.uv = input.uv * constants.uvRect.zw + constants.uvRect.xy;

    //calc mask
    output.maskMin = mul(viewProjection, float4(constants.mask.xy, 0.0f, 1.0f)).xy;
    output.maskMax = mul(viewProjection, float4(constants.mask.zw, 0.0f, 1.0f)).xy;
    output.clipPos = output.position.xy;

  return output;
}

[shader("pixel")]
float4 MainPS(V2F input) : SV_TARGET {
    //discard if outside mask
    if (input.clipPos.x < input.maskMin.x || 
    input.clipPos.x > input.maskMax.x || 
    input.clipPos.y < input.maskMin.y || 
    input.clipPos.y > input.maskMax.y)
    {
        discard;
    }
    float4 color = _texture.Sample(_textureSampler, input.uv) * constants.color;
#if defined(ALPHA_TEST)
  if (color.a < 0.01f)
  {
    discard;
  }
#endif
  return color;
}