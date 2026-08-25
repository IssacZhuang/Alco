// file "reinhard-luminance-tonemap.slang" - module "reinhard_luminance_tonemap"
//
// Slang module converted from Shaders/Pipelines/PostProcess/Tonemap/ReinhardLuminanceTonemap.hlsl (Phase 2 migration).
// Resources are grouped as ParameterBlocks — the compiler assigns each block
// its own descriptor set in declaration order; members resolve by bare field name.

#language slang 2025
module reinhard_luminance_tonemap;

import alco_rendering_core;

public struct PassParams
{
    public Texture2D _texture; // should be HDR image
};

public ParameterBlock<PassParams> _pass;
public struct DataParams
{
    public float MaxLuminance;
    public float Gamma;
};

public ParameterBlock<DataParams> _data;


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

float3 change_luminance(float3 color, float new_luminance) {
  return color * (new_luminance / luminance(color));
}

[shader("fragment")]
float4 MainPS(V2F input) : SV_TARGET {
  float4 source = _pass._texture.Sample(_samplers._linearClamp, input.uv);

  float old_luminance = dot(source.rgb, float3(0.2126, 0.7152, 0.0722));
  float numerator = old_luminance * (1.0 + (old_luminance/(_data.MaxLuminance*_data.MaxLuminance)));
  float new_luminance = numerator / (1.0 + old_luminance);

  float3 ldrColor = change_luminance(source.rgb, new_luminance);
  ldrColor = pow(ldrColor, 1.0 / _data.Gamma);
  ldrColor += OutputDither8Bit(input.position.xy);
  return float4(ldrColor, 1.0);
}