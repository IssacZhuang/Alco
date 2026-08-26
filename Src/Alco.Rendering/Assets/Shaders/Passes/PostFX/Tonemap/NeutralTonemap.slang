// file "NeutralTonemap.slang" - module "NeutralTonemap"
//
// Slang module converted from Shaders/Pipelines/PostProcess/Tonemap/NeutralTonemap.hlsl (Phase 2 migration).
// Resources are grouped as ParameterBlocks — the compiler assigns each block
// its own descriptor set in declaration order; members resolve by bare field name.

#language slang 2025
module NeutralTonemap;

import AlcoRendering_Core;

// Texture input and parameters
public struct PassParams
{
    public Texture2D texture;
};

public ParameterBlock<PassParams> pass;
public struct DataParams
{
    // exposure multiplier applied before tone mapping
    public float exposure;
    // gamma value for final gamma correction
    public float gamma;
    // Start of compression (linear region end)
    public float startCompression;
    // desaturation factor in compression region
    public float desaturation;
};

public ParameterBlock<DataParams> data;


struct Vertex { float3 position : POSITION; float2 uv : TEXCOORD0; };
struct V2F { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };

// Khronos PBR Neutral tone mapping (ported from pbrNeutral.glsl)
// Reference: https://github.com/KhronosGroup/ToneMapping/blob/main/PBR_Neutral/pbrNeutral.glsl

float3 pBRNeutralToneMap(float3 color)
{
    // Toe & offset
    float x = min(color.r, min(color.g, color.b));
    float offset = (x < 0.08f) ? (x - 6.25f * x * x) : 0.04f;
    color -= offset;

    // If below compression start, return early
    float peak = max(color.r, max(color.g, color.b));
    if (peak < data.startCompression)
    {
        return color;
    }

    // Compress peak to fit in [0,1]
    float d = 1.0f - data.startCompression;
    float newPeak = 1.0f - (d * d) / (peak + d - data.startCompression);
    color *= (newPeak / peak);

    // Desaturate towards white based on removed brightness
    float g = 1.0f - 1.0f / (data.desaturation * (peak - newPeak) + 1.0f);
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
    float3 hdr = pass.texture.Sample(samplers.linearClamp, input.uv).rgb * data.exposure;
    float3 ldr = pBRNeutralToneMap(hdr);
    ldr = pow(ldr, 1.0 / data.gamma);
    ldr += outputDither8Bit(input.position.xy);
    return float4(ldr, 1.0);
}

