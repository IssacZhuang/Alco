#include "Assets/Shader/Lib/Core.hlsli"

struct VS_IN 
{
  float3 position : POSITION;
  float2 uv : TEXCOORD;
  float4 color : COLOR;
};

struct PS_IN 
{
  float4 position : SV_POSITION;
  float4 color : COLOR;
  float2 uv : TEXCOORD;
};

PS_IN VS(VS_IN input) 
{
  PS_IN output = (PS_IN)0;

  output.position = VertexToClipSpace(input.position);
  output.color = input.color;
  output.uv = input.uv;

  return output;
}

float4 PS(PS_IN input) : SV_Target 
{ 
    return input.color; 
}