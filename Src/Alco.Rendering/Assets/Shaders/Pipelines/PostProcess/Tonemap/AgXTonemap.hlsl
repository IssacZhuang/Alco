#include "Shaders/Libs/Core.hlsli"

// Texture input and parameters
DEFINE_TEX2D_SAMPLE(0, _texture);
DEFINE_UNIFORM(1, _data){
    float Exposure;
    float Gamma;
    float Look; // 0: Default, 1: Golden, 2: Punchy
};

struct Vertex { float3 position : POSITION; float2 uv : TEXCOORD0; };
struct V2F { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };

// AgX tonemapping — minimal implementation
// Reference: https://iolite-engine.com/blog_posts/minimal_agx_implementation
// Values derived from Troy Sobotka's AgX: https://github.com/sobotka/AgX
// MIT License, Copyright (c) 2024 Missing Deadlines (Benjamin Wrensch)

// Mean error^2: 3.6705141e-06
float3 AgxDefaultContrastApprox(float3 x)
{
    const float3 x2 = x * x;
    const float3 x4 = x2 * x2;

    return + 15.5    * x4 * x2
           - 40.14   * x4 * x
           + 31.96   * x4
           - 6.868   * x2 * x
           + 0.4298  * x2
           + 0.1191  * x
           - 0.00232;
}

float3 Agx(float3 val)
{
    const float3x3 agx_mat = float3x3(
        0.842479062253094, 0.0423282422610123, 0.0423756549057051,
        0.0784335999999992, 0.878468636469772, 0.0784336,
        0.0792237451477643, 0.0791661274605434, 0.879142973793104);

    // Log2 space encoding bounds
    // Derived from DEFAULT_LOG2_MIN=-10, DEFAULT_LOG2_MAX=6.5, MIDDLE_GRAY=0.18
    const float min_ev = -12.47393f;
    const float max_ev = 4.026069f;

    // Input transform (inset)
    val = mul(val, agx_mat);

    // Log2 space encoding
    val = clamp(log2(val), min_ev, max_ev);
    val = (val - min_ev) / (max_ev - min_ev);

    // Apply sigmoid function approximation
    val = AgxDefaultContrastApprox(val);

    return val;
}

float3 AgxEotf(float3 val)
{
    const float3x3 agx_mat_inv = float3x3(
        1.19687900512017, -0.0528968517574562, -0.0529716355144438,
        -0.0980208811401368, 1.15190312990417, -0.0980434501171241,
        -0.0990297440797205, -0.0989611768448433, 1.15107367264116);

    // Inverse input transform (outset)
    val = mul(val, agx_mat_inv);

    return val;
}

float3 AgxLook(float3 val, float look)
{
    const float3 lw = float3(0.2126, 0.7152, 0.0722);
    const float luma = dot(val, lw);

    // Default
    float3 offset = float3(0.0, 0.0, 0.0);
    float3 slope = float3(1.0, 1.0, 1.0);
    float3 power = float3(1.0, 1.0, 1.0);
    float sat = 1.0;

    if (look < 0.5)
    {
        // Default — no changes
    }
    else if (look < 1.5)
    {
        // Golden
        slope = float3(1.0, 0.9, 0.5);
        power = float3(0.8, 0.8, 0.8);
        sat = 0.8;
    }
    else
    {
        // Punchy
        power = float3(1.35, 1.35, 1.35);
        sat = 1.4;
    }

    // ASC CDL
    val = pow(val * slope + offset, power);
    return luma + sat * (val - luma);
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
    float3 ldr = Agx(hdr);
    ldr = AgxLook(ldr, Look);
    ldr = AgxEotf(ldr);
    ldr = pow(max(ldr, 0.0), 1.0 / Gamma);
    ldr += OutputDither8Bit(input.position.xy);
    return float4(ldr, 1.0);
}
