cbuffer GlobalBuffer : register(b0, space0) {
  float4x4 _ViewProjMatrix;
  float2 _ScreenSize;
  float _Time;
  float _DeltaTime;
  float _SinTime;
  float _CosTime;
};

cbuffer TransformBuffer : register(b0, space1) { float4x4 _TransformMatrix; };

#define PI 3.141592
#define TAU 6.283185
#define E 2.718281

float4 MakeClipSpaceConsistent(float4 vertex) {
#ifdef BACKEND_OPENGL
  vertex.z = vertex.z * 2.0 - vertex.w;
#endif

#ifdef BACKEND_OPENGLES
  vertex.z = vertex.z * 2.0 - vertex.w;
#endif

#ifdef BACKEND_VULKAN
  vertex.y = -vertex.y;
#endif
  return vertex;
}

float2 MakeUVCosistent(float2 uv) {
#ifdef BACKEND_OPENGL
  uv.y = 1.0 - uv.y;
#endif

#ifdef BACKEND_OPENGLES
  uv.y = 1.0 - uv.y;
#endif
  return uv;
}

float4 VertexToClipSpace(float4 vertex) {
  float4 pos = mul(_TransformMatrix, vertex);
  return MakeClipSpaceConsistent(mul(_ViewProjMatrix, pos));
}

float4 VertexToClipSpace(float3 vertex) {
  return VertexToClipSpace(float4(vertex, 1.0));
}

float4 SampleTex2D(Texture2D tex, SamplerState texSampler, float2 uv) {
  uv = MakeUVCosistent(uv);
  return tex.Sample(texSampler, uv);
}
