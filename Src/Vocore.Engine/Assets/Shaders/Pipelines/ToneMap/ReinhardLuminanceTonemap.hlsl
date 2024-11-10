#include "Shaders/Libs/Core.hlsli"

DEFINE_TEX2D_SAMPLE(0, _texture); // should be HDR image
DEFINE_UNIFORM(1, _data){
    float MaxLuminance;
    float Gamma;
};

struct Vertex2D {
  float2 position : POSITION;
  float2 uv : TEXCOORD0;
};

struct V2F {
  float4 position : SV_POSITION;
  float2 uv : TEXCOORD0;
};

[shader("vertex")]
V2F MainVS(Vertex2D input) {
  V2F output = (V2F)0;
  output.position = float4(input.position, 0.0f, 1.0f);
  output.uv = input.uv;
  return output;
}

float luminance(float3 color) {
  return dot(color, float3(0.2126, 0.7152, 0.0722));
}

float3 change_luminance(float3 color, float new_luminance) {
  return color * (new_luminance / luminance(color));
}

[shader("pixel")]
float4 MainPS(V2F input) : SV_TARGET {
  float4 source = SAMPLE_TEX2D(_texture, input.uv);

  float old_luminance = dot(source.rgb, float3(0.2126, 0.7152, 0.0722));
  float numerator = old_luminance * (1.0 + (old_luminance/(MaxLuminance*MaxLuminance)));
  float new_luminance = numerator / (1.0 + old_luminance);

  float3 ldrColor = change_luminance(source.rgb, new_luminance);
  ldrColor = pow(ldrColor, 1.0 / Gamma);
  return float4(ldrColor, 1.0);
}