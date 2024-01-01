#include "Assets/ShaderLib/Base.hlsli"

cbuffer GlobalBuffer : SLOT(0) {
  float4x4 _ViewProjMatrix;
  float2 _ScreenSize;
  float _Time;
  float _DeltaTime;
  float _SinTime;
  float _CosTime;
};

cbuffer TransformBuffer : SLOT(1) { float4x4 _TransformMatrix; };

#define PI 3.141592
#define TAU 6.283185
#define E 2.718281

float4 VertexToClipSpace(float4 vertex) {
  return VertexToClipSpace(vertex, _TransformMatrix, _ViewProjMatrix);
}

float4 VertexToClipSpace(float3 vertex) {
  return VertexToClipSpace(float4(vertex, 1), _TransformMatrix, _ViewProjMatrix);
}