// file "Text.slang" - module "Text"
//
// Resources are grouped as ParameterBlocks — the compiler assigns each block
// its own descriptor set in declaration order; members resolve by bare field name.

#language slang 2025
module Text;

import AlcoRendering_Core;

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

public ParameterBlock<CameraParams> camera;


public struct TextBufferParams
{
    public RWStructuredBuffer<TextData> textBuffer;
};

public ParameterBlock<TextBufferParams> textBuffer;

public struct MaterialParamsData
{
    public Texture2D font;
};

public ParameterBlock<MaterialParamsData> material;

[[vk::push_constant]] Constants constants;

[shader("vertex")]
V2F MainVS(Vertex input) {
  TextData data = textBuffer.textBuffer[input.instanceID];
  float3 vertexPos = input.position * float3(data.size, 1.0f);
  float3 offset = float3(data.offset + constants.vertexOffset, 0.0f);
  float4 position = float4(vertexPos + offset, 1.0f);
  position = mul(constants.model, position);
  position = mul(camera.viewProjection, position);

  V2F output = (V2F)0;
  output.position = position;
  output.uv = input.uv;
  output.instanceID = input.instanceID;
  return output;
}

[shader("fragment")]
float4 MainPS(V2F input) : SV_TARGET {
  TextData data = textBuffer.textBuffer[input.instanceID];
  float2 uv = input.uv * data.uvRect.zw + data.uvRect.xy;
  float r = material.font.Sample(samplers.linearRepeat, uv).r;
  // Emit premultiplied alpha for BlendState.PremultipliedAlpha (src=One, dst=OneMinusSrcAlpha).
  // Glyph coverage (r) is the geometric alpha; multiplying it with data.color.a gives the final
  // alpha, then rgb is premultiplied by that alpha. This zeroes both rgb and A when the parent
  // opacity is 0 (e.g. XP notification fade-out), instead of leaking rgb through a zero alpha.
  float a = r * data.color.a;
  return float4(data.color.rgb * a, a);
}
