// file "Text.slang" - module "text"
//
// Slang module converted from Shaders/Pipelines/Rendering/Text/Text.hlsl (Phase 2 migration).
// DEFINE_* macros became explicit set/binding declarations; bindings within a
// set are assigned in the original declaration order and are compiler-owned.

#language slang 2025
module text;

import alco_rendering_core;

// Transitional sampling shorthands: the preprocessor does not cross module
// boundaries, so the old Core.hlsli macros live here until Phase 3 deletes the
// name##Sampler convention outright.
#define SAMPLE_TEX2D(textureName, uv) textureName.Sample(textureName##Sampler, uv)
#define SAMPLE_TEX2D_LEVEL(textureName, uv, mip) textureName.SampleLevel(textureName##Sampler, uv, mip)
#define SAMPLE_TEX3D_LEVEL(textureName, uvw, mip) textureName.SampleLevel(textureName##Sampler, uvw, mip)
#define GET_PIXEL_TEX2D(textureName, position) textureName.Load(int3(position, 0))
#define LOAD_TEX3D(textureName, coord, mip) textureName.Load(int4(coord, mip))
#define SAMPLE_TEX2D_DEPTH_CMP(textureName, uv, compareDepth) textureName.SampleCmpLevelZero(textureName##Sampler, uv, compareDepth)

#define MAX_INSTANCE_COUNT 300


struct Vertex {
  float3 position : POSITION;
  float2 uv : TEXCOORD0;
  uint instanceId : SV_VulkanInstanceID;
};

struct V2F {
  float4 position : SV_POSITION;
  float2 uv : TEXCOORD0;
  uint instanceId : TEXCOORD1;
};

struct Constants {
  float4x4 model;
  float2 vertexOffset; // to offset the text to pivot point
};

struct TextData {
  float4 uvRect;
  float4 color;
  float2 offset;
  float2 size;
};

[[vk::binding(0, 0)]] cbuffer _camera { float4x4 viewProjection; };


[[vk::binding(0, 1)]] RWStructuredBuffer<TextData> _textBuffer;

[[vk::binding(0, 2)]] Texture2D _font;
[[vk::binding(1, 2)]] SamplerState _fontSampler;

[[vk::push_constant]] Constants constants;

[shader("vertex")]
V2F MainVS(Vertex input) {
  TextData data = _textBuffer[input.instanceId];
  float3 vertexPos = input.position * float3(data.size, 1.0f);
  float3 offset = float3(data.offset + constants.vertexOffset, 0.0f);
  float4 position = float4(vertexPos + offset, 1.0f);
  position = mul(constants.model, position);
  position = mul(viewProjection, position);

  V2F output = (V2F)0;
  output.position = position;
  output.uv = input.uv;
  output.instanceId = input.instanceId;
  return output;
}

[shader("fragment")]
float4 MainPS(V2F input) : SV_TARGET {
  TextData data = _textBuffer[input.instanceId];
  float2 uv = input.uv * data.uvRect.zw + data.uvRect.xy;
  float r = SAMPLE_TEX2D(_font, uv).r;
#if defined(ALPHA_TEST)
  if (r < 0.01f)
  {
    discard;
  }
#endif
  // Emit premultiplied alpha for BlendState.PremultipliedAlpha (src=One, dst=OneMinusSrcAlpha).
  // Glyph coverage (r) is the geometric alpha; multiplying it with data.color.a gives the final
  // alpha, then RGB is premultiplied by that alpha. This zeroes both RGB and A when the parent
  // opacity is 0 (e.g. XP notification fade-out), instead of leaking RGB through a zero alpha.
  float a = r * data.color.a;
  return float4(data.color.rgb * a, a);
}
