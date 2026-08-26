// file "ReinhardLuminanceTonemap.slang" - module "ReinhardLuminanceTonemap"
//
// Slang module converted from Shaders/Pipelines/PostProcess/Tonemap/ReinhardLuminanceTonemap.hlsl (Phase 2 migration).
// Resources are grouped as ParameterBlocks — the compiler assigns each block
// its own descriptor set in declaration order; members resolve by bare field name.

#language slang 2025
module ReinhardLuminanceTonemap;

import AlcoRendering_Core;

public struct PassParams
{
    public Texture2D texture; // should be HDR image
};

public ParameterBlock<PassParams> pass;
public struct DataParams
{
    public float maxLuminance;
    public float gamma;
};

public ParameterBlock<DataParams> data;


struct Vertex {
  float3 position : POSITION;
  float2 uv : TEXCOORD0;
};

struct V2F {
  float4 position : SV_POSITION;
  float2 uv : TEXCOORD0;
};

[shader("vertex")]
V2F MainVS(Vertex input) {
  V2F output = (V2F)0;
  output.position = float4(input.position, 1.0f);
  output.uv = input.uv;
  return output;
}

float luminance(float3 color) {
  return dot(color, float3(0.2126, 0.7152, 0.0722));
}

float3 changeLuminance(float3 color, float newLuminance) {
  return color * (newLuminance / luminance(color));
}

[shader("fragment")]
float4 MainPS(V2F input) : SV_TARGET {
  float4 source = pass.texture.Sample(samplers.linearClamp, input.uv);

  float oldLuminance = dot(source.rgb, float3(0.2126, 0.7152, 0.0722));
  float numerator = oldLuminance * (1.0 + (oldLuminance/(data.maxLuminance*data.maxLuminance)));
  float newLuminance = numerator / (1.0 + oldLuminance);

  float3 ldrColor = changeLuminance(source.rgb, newLuminance);
  ldrColor = pow(ldrColor, 1.0 / data.gamma);
  ldrColor += outputDither8Bit(input.position.xy);
  return float4(ldrColor, 1.0);
}