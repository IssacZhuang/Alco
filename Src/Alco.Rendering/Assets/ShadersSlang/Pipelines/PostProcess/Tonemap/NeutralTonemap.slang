// file "NeutralTonemap.slang" - module "neutral_tonemap"
//
// Slang module converted from Shaders/Pipelines/PostProcess/Tonemap/NeutralTonemap.hlsl (Phase 2 migration).
// DEFINE_* macros became explicit set/binding declarations; bindings within a
// set are assigned in the original declaration order and are compiler-owned.

#language slang 2025
module neutral_tonemap;

import alco_rendering_core;

// Transitional sampling shorthands: the preprocessor does not cross module
// boundaries, so the old Core.hlsli macros live here until Phase 3 deletes the
// name##Sampler convention outright.
#define SAMPLE_TEX2D(textureName, uv) textureName.Sample(textureName##Sampler, uv)
#define SAMPLE_TEX2D_LEVEL(textureName, uv, mip) textureName.SampleLevel(textureName##Sampler, uv, mip)
#define SAMPLE_TEX3D_LEVEL(textureName, uvw, mip) textureName.SampleLevel(textureName##Sampler, uvw, mip)
#define GET_PIXEL_TEX2D(textureName, position) textureName.Load(int3(position, 0))
#define LOAD_TEX3D(textureName, coord, mip) textureName.Load(int4(coord, mip))
#define SAMPLE_TEX2D_DEPTH_CMP(textureName, uv, compareDepth) textureName.SampleCmpLevelZero(textureName##Sampler, uv, compareDepth)

// Texture input and parameters
[[vk::binding(0, 0)]] Texture2D _texture;
[[vk::binding(1, 0)]] SamplerState _textureSampler;
[[vk::binding(0, 1)]] cbuffer _data {
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

[shader("fragment")]
float4 MainPS(V2F input): SV_TARGET
{
    float3 hdr = SAMPLE_TEX2D(_texture, input.uv).rgb * Exposure;
    float3 ldr = PBRNeutralToneMap(hdr);
    ldr = pow(ldr, 1.0 / Gamma);
    ldr += OutputDither8Bit(input.position.xy);
    return float4(ldr, 1.0);
}

