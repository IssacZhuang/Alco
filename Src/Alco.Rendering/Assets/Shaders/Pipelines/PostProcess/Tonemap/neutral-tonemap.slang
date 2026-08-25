// file "neutral-tonemap.slang" - module "neutral_tonemap"
//
// Slang module converted from Shaders/Pipelines/PostProcess/Tonemap/NeutralTonemap.hlsl (Phase 2 migration).
// Resources are grouped as ParameterBlocks — the compiler assigns each block
// its own descriptor set in declaration order; members resolve by bare field name.

#language slang 2025
module neutral_tonemap;

import alco_rendering_core;

// Texture input and parameters
public struct PassParams
{
    public Texture2D _texture;
    public SamplerState _textureSampler;
};

public ParameterBlock<PassParams> _pass;
public struct DataParams
{
    // Exposure multiplier applied before tone mapping
    public float Exposure;
    // Gamma value for final gamma correction
    public float Gamma;
    // Start of compression (linear region end)
    public float StartCompression;
    // Desaturation factor in compression region
    public float Desaturation;
};

public ParameterBlock<DataParams> _data;


struct Vertex { float3 position : POSITION; float2 uv : TEXCOORD0; };
struct V2F { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };

// Khronos PBR Neutral tone mapping (ported from pbrNeutral.glsl)
// Reference: https://github.com/KhronosGroup/ToneMapping/blob/main/PBR_Neutral/pbrNeutral.glsl

float3 PBRNeutralToneMap(float3 color)
{
    // Toe & offset
    float x = min(color.r, min(color.g, color.b));
    float offset = (x < 0.08f) ? (x - 6.25f * x * x) : 0.04f;
    color -= offset;

    // If below compression start, return early
    float peak = max(color.r, max(color.g, color.b));
    if (peak < _data.StartCompression)
    {
        return color;
    }

    // Compress peak to fit in [0,1]
    float d = 1.0f - _data.StartCompression;
    float newPeak = 1.0f - (d * d) / (peak + d - _data.StartCompression);
    color *= (newPeak / peak);

    // Desaturate towards white based on removed brightness
    float g = 1.0f - 1.0f / (_data.Desaturation * (peak - newPeak) + 1.0f);
    float3 white = float3(newPeak, newPeak, newPeak);
    color = lerp(color, white, g);
    return color;
}

[shader("vertex")]
V2F MainVS(Vertex input)
{
    V2F o = (V2F)0; o.position = float4(input.position, 1); o.uv = input.uv; return o;
}

[shader("fragment")]
float4 MainPS(V2F input): SV_TARGET
{
    float3 hdr = _pass._texture.Sample(_pass._textureSampler, input.uv).rgb * _data.Exposure;
    float3 ldr = PBRNeutralToneMap(hdr);
    ldr = pow(ldr, 1.0 / _data.Gamma);
    ldr += OutputDither8Bit(input.position.xy);
    return float4(ldr, 1.0);
}

