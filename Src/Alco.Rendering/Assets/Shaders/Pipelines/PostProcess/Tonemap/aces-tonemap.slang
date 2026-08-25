// file "aces-tonemap.slang" - module "a_c_e_s_tonemap"
//
// Slang module converted from Shaders/Pipelines/PostProcess/Tonemap/ACESTonemap.hlsl (Phase 2 migration).
// Resources are grouped as ParameterBlocks — the compiler assigns each block
// its own descriptor set in declaration order; members resolve by bare field name.

#language slang 2025
module aces_tonemap;

import alco_rendering_core;

public struct PassParams
{
    public Texture2D _texture;
};

public ParameterBlock<PassParams> _pass;
public struct DataParams
{
    public float A; // 2.51 (default)
    public float B; // 0.03
    public float C; // 2.43
    public float D; // 0.59
    public float E; // 0.14
    public float Exposure;
    public float Gamma;
};

public ParameterBlock<DataParams> _data;


struct Vertex { float3 position : POSITION; float2 uv : TEXCOORD0; };
struct V2F { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };

float3 ACES(float3 x) {
  return clamp((x * (_data.A * x + _data.B)) / (x * (_data.C * x + _data.D) + _data.E), 0.0, 1.0);
}

[shader("vertex")]
V2F MainVS(Vertex input){ V2F o=(V2F)0; o.position=float4(input.position,1); o.uv=input.uv; return o; }

[shader("fragment")]
float4 MainPS(V2F input): SV_TARGET
{
    float3 v = _pass._texture.Sample(_samplers._linearClamp, input.uv).rgb * _data.Exposure;
    float3 ldr = ACES(v);
    ldr = pow(ldr, 1.0 / _data.Gamma);
    ldr += OutputDither8Bit(input.position.xy);
    return float4(ldr, 1.0);
}

