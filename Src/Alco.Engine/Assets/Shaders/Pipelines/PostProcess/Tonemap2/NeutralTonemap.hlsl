#include "Shaders/Libs/Core.hlsli"

// Texture input and parameters
DEFINE_TEX2D_SAMPLE(0, _texture);
DEFINE_UNIFORM(1, _data){
    // Exposure multiplier applied before tone mapping
    float Exposure;
    // Gamma value for final gamma correction
    float Gamma;
    // Start of compression (linear region end)
    float StartCompression;
    // Desaturation factor in compression region
    float Desaturation;
};

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
    if (peak < StartCompression)
    {
        return color;
    }

    // Compress peak to fit in [0,1]
    float d = 1.0f - StartCompression;
    float newPeak = 1.0f - (d * d) / (peak + d - StartCompression);
    color *= (newPeak / peak);

    // Desaturate towards white based on removed brightness
    float g = 1.0f - 1.0f / (Desaturation * (peak - newPeak) + 1.0f);
    float3 white = float3(newPeak, newPeak, newPeak);
    color = lerp(color, white, g);
    return color;
}

[shader("vertex")]
V2F MainVS(Vertex input)
{
    V2F o = (V2F)0; o.position = float4(input.position, 1); o.uv = input.uv; return o;
}

[shader("pixel")]
float4 MainPS(V2F input): SV_TARGET
{
    float3 hdr = SAMPLE_TEX2D(_texture, input.uv).rgb * Exposure;
    float3 ldr = PBRNeutralToneMap(hdr);
    ldr = pow(ldr, 1.0 / Gamma);
    return float4(ldr, 1.0);
}

