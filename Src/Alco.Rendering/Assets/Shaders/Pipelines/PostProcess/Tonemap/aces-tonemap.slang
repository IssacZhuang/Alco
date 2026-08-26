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
    public Texture2D texture;
};

public ParameterBlock<PassParams> pass;
public struct DataParams
{
    public float a; // 2.51 (default)
    public float b; // 0.03
    public float c; // 2.43
    public float d; // 0.59
    public float e; // 0.14
    public float exposure;
    public float gamma;
};

public ParameterBlock<DataParams> data;


struct Vertex { float3 position : POSITION; float2 uv : TEXCOORD0; };
struct V2F { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };

float3 aces(float3 x) {
  return clamp((x * (data.a * x + data.b)) / (x * (data.c * x + data.d) + data.e), 0.0, 1.0);
}

[shader("vertex")]
V2F MainVS(Vertex input){ V2F o=(V2F)0; o.position=float4(input.position,1); o.uv=input.uv; return o; }

[shader("fragment")]
float4 MainPS(V2F input): SV_TARGET
{
    float3 v = pass.texture.Sample(samplers.linearClamp, input.uv).rgb * data.exposure;
    float3 ldr = aces(v);
    ldr = pow(ldr, 1.0 / data.gamma);
    ldr += outputDither8Bit(input.position.xy);
    return float4(ldr, 1.0);
}

