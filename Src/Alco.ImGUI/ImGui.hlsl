// from "Shaders/Libs/Core.hlsli"
#define vk_binding vk::binding
#define vk_push_constant vk::push_constant

#define SLOT(set, bind) [[vk_binding(bind, set)]] // layout(binding = bind, set = set)
#define DEFINE_UNIFORM(index, name) SLOT(index, 0) cbuffer name
#define DEFINE_TEX2D_SAMPLE(index, name) SLOT(index, 0) Texture2D name; SLOT(index, 1) SamplerState name##Sampler
#define PUSH_CONSTANT [[vk::push_constant]] // layout(push_constant) in GLSL

struct Vertex {
  float2 position : POSITION;
  float2 uv : TEXCOORD0;
  float4 color : COLOR;
};

struct V2F {
  float4 position : SV_POSITION;
  float2 uv : TEXCOORD0;
  float4 color : COLOR;
};

DEFINE_UNIFORM(0, _camera) { float4x4 viewProjection; };

DEFINE_TEX2D_SAMPLE(1, _texture);


[shader("vertex")]
V2F MainVS(Vertex input) {
  V2F output = (V2F)0;
  output.position = mul(viewProjection, float4(input.position, 0.0f, 1.0f));
  output.uv = input.uv;
  output.color = input.color;
  return output;
}

[shader("pixel")]
float4 MainPS(V2F input) : SV_TARGET {
  float4 color = _texture.Sample(_textureSampler, input.uv);
  return color * input.color;
}