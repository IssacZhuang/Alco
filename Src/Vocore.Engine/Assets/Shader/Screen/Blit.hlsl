#include "Base.hlsl"

struct VS_IN {
  float3 position : POSITION;
  float2 uv : TEXCOORD;
};

struct PS_IN {
  float4 position : SV_POSITION;
  float2 uv : TEXCOORD;
};

cbuffer BlitTexture : register(b0, space0) {
  Texture2D _MainTex;
  SamplerState _MainTexSampler;
};

PS_IN VS(VS_IN input) {
  PS_IN output = (PS_IN)0;

  output.position = MakeClipSpaceConsistent(input.position);
  output.uv = input.uv;

  return output;
}

float4 PS(PS_IN input) : SV_Target {
    return SampleTex2D(_MainTex, _MainTexSampler, input.uv)
}