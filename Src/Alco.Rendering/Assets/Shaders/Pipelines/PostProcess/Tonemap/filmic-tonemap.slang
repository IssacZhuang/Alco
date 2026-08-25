// file "filmic-tonemap.slang" - module "filmic_tonemap"
//
// Slang module converted from Shaders/Pipelines/PostProcess/Tonemap/FilmicTonemap.hlsl (Phase 2 migration).
// Resources are grouped as ParameterBlocks — the compiler assigns each block
// its own descriptor set in declaration order; members resolve by bare field name.

#language slang 2025
module filmic_tonemap;

import alco_rendering_core;

public struct PassParams
{
    public Texture2D _texture;
};

public ParameterBlock<PassParams> _pass;
public struct DataParams
{
    public float Exposure;
    public float Gamma;
};

public ParameterBlock<DataParams> _data;


struct Vertex { float3 position : POSITION; float2 uv : TEXCOORD0; };
struct V2F { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };

float3 Filmic(float3 x) {
  float3 X = max(float3(0,0,0), x - 0.004);
  float3 result = (X * (6.2 * X + 0.5)) / (X * (6.2 * X + 1.7) + 0.06);
  return result;
}

[shader("vertex")]
V2F MainVS(Vertex input){ V2F o=(V2F)0; o.position=float4(input.position,1); o.uv=input.uv; return o; }

[shader("fragment")]
float4 MainPS(V2F input): SV_TARGET
{
    float3 v = _pass._texture.Sample(_samplers._linearClamp, input.uv).rgb * _data.Exposure;
    float3 ldr = Filmic(v);
    ldr = pow(ldr, 1.0 / _data.Gamma);
    ldr += OutputDither8Bit(input.position.xy);
    return float4(ldr, 1.0);
}

