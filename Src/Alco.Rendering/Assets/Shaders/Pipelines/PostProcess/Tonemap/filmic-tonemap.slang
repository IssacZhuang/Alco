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
    public Texture2D texture;
};

public ParameterBlock<PassParams> pass;
public struct DataParams
{
    public float exposure;
    public float gamma;
};

public ParameterBlock<DataParams> data;


struct Vertex { float3 position : POSITION; float2 uv : TEXCOORD0; };
struct V2F { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };

float3 filmic(float3 x) {
  float3 xc = max(float3(0,0,0), x - 0.004);
  float3 result = (xc * (6.2 * xc + 0.5)) / (xc * (6.2 * xc + 1.7) + 0.06);
  return result;
}

[shader("vertex")]
V2F MainVS(Vertex input){ V2F o=(V2F)0; o.position=float4(input.position,1); o.uv=input.uv; return o; }

[shader("fragment")]
float4 MainPS(V2F input): SV_TARGET
{
    float3 v = pass.texture.Sample(samplers.linearClamp, input.uv).rgb * data.exposure;
    float3 ldr = filmic(v);
    ldr = pow(ldr, 1.0 / data.gamma);
    ldr += outputDither8Bit(input.position.xy);
    return float4(ldr, 1.0);
}

