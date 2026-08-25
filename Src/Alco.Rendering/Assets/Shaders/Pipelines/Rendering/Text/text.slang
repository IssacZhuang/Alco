// file "text.slang" - module "text"
//
// Slang module converted from Shaders/Pipelines/Rendering/Text/Text.hlsl (Phase 2 migration).
// Resources are grouped as ParameterBlocks — the compiler assigns each block
// its own descriptor set in declaration order; members resolve by bare field name.

#language slang 2025
module text;

import alco_rendering_core;

#define MAX_INSTANCE_COUNT 300


struct Vertex {
  float3 position : POSITION;
  float2 uv : TEXCOORD0;
  uint instanceID : SV_VulkanInstanceID;
};

struct V2F {
  float4 position : SV_POSITION;
  float2 uv : TEXCOORD0;
  uint instanceID : TEXCOORD1;
};

public struct Constants {
  float4x4 model;
  float2 vertexOffset; // to offset the text to pivot point
};

public struct TextData {
  float4 uvRect;
  float4 color;
  float2 offset;
  float2 size;
};

public struct CameraParams
{
    public float4x4 viewProjection;
};

public ParameterBlock<CameraParams> _camera;


public struct TextBufferParams
{
    public RWStructuredBuffer<TextData> _textBuffer;
};

public ParameterBlock<TextBufferParams> _textBuffer;

public struct MaterialParamsData
{
    public Texture2D _font;
};

public ParameterBlock<MaterialParamsData> _material;

[[vk::push_constant]] Constants constants;

[shader("vertex")]
V2F MainVS(Vertex input) {
  TextData data = _textBuffer._textBuffer[input.instanceID];
  float3 vertexPos = input.position * float3(data.size, 1.0f);
  float3 offset = float3(data.offset + constants.vertexOffset, 0.0f);
  float4 position = float4(vertexPos + offset, 1.0f);
  position = mul(constants.model, position);
  position = mul(_camera.viewProjection, position);

  V2F output = (V2F)0;
  output.position = position;
  output.uv = input.uv;
  output.instanceID = input.instanceID;
  return output;
}

[shader("fragment")]
float4 MainPS(V2F input) : SV_TARGET {
  TextData data = _textBuffer._textBuffer[input.instanceID];
  float2 uv = input.uv * data.uvRect.zw + data.uvRect.xy;
  float r = _material._font.Sample(_samplers._linearRepeat, uv).r;
  // Emit premultiplied alpha for BlendState.PremultipliedAlpha (src=One, dst=OneMinusSrcAlpha).
  // Glyph coverage (r) is the geometric alpha; multiplying it with data.color.a gives the final
  // alpha, then RGB is premultiplied by that alpha. This zeroes both RGB and A when the parent
  // opacity is 0 (e.g. XP notification fade-out), instead of leaking RGB through a zero alpha.
  float a = r * data.color.a;
  return float4(data.color.rgb * a, a);
}
