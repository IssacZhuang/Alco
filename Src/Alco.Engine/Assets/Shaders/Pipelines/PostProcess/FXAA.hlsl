#include "Shaders/Libs/Core.hlsli"

DEFINE_TEX2D_SAMPLE(0, _texture);

// FXAA configuration constants
DEFINE_UNIFORM(1, _fxaaData) { 
    float2 InvFrameSize;      // 1.0 / FrameSize
    float Quality;            // Quality setting (0.5-2.0)
    float Threshold;          // Edge detection threshold (0.063-0.333)
};

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

// Calculate luminance using standard coefficients
float GetLuminance(float3 color) {
    return dot(color, float3(0.299, 0.587, 0.114));
}

// FXAA constants
static const float FXAA_SPAN_MAX = 8.0;
static const float FXAA_REDUCE_MUL = 1.0 / 8.0;
static const float FXAA_REDUCE_MIN = 1.0 / 128.0;

// Improved FXAA implementation based on reference algorithms
float3 FXAA(float2 uv) {
    float2 texelSize = InvFrameSize;

    // Sample the 5 necessary pixels: center + 4 corners
    float3 rgbNW = SAMPLE_TEX2D(_texture, uv + float2(-1.0, -1.0) * texelSize).rgb;
    float3 rgbNE = SAMPLE_TEX2D(_texture, uv + float2(1.0, -1.0) * texelSize).rgb;
    float3 rgbSW = SAMPLE_TEX2D(_texture, uv + float2(-1.0, 1.0) * texelSize).rgb;
    float3 rgbSE = SAMPLE_TEX2D(_texture, uv + float2(1.0, 1.0) * texelSize).rgb;
    float3 rgbM = SAMPLE_TEX2D(_texture, uv).rgb;

    // Convert to luminance
    float3 luma = float3(0.299, 0.587, 0.114);
    float lumaNW = dot(rgbNW, luma);
    float lumaNE = dot(rgbNE, luma);
    float lumaSW = dot(rgbSW, luma);
    float lumaSE = dot(rgbSE, luma);
    float lumaM = dot(rgbM, luma);

    // Find min and max luminance
    float lumaMin = min(lumaM, min(min(lumaNW, lumaNE), min(lumaSW, lumaSE)));
    float lumaMax = max(lumaM, max(max(lumaNW, lumaNE), max(lumaSW, lumaSE)));

    // Early exit if contrast is too low
    float lumaRange = lumaMax - lumaMin;
    if (lumaRange < max(Threshold, lumaMax * 0.125)) {
        return rgbM;
    }

    // Calculate the blur direction
    float2 dir;
    dir.x = -((lumaNW + lumaNE) - (lumaSW + lumaSE));
    dir.y = ((lumaNW + lumaSW) - (lumaNE + lumaSE));

    // Apply the reduce threshold
    float dirReduce = max((lumaNW + lumaNE + lumaSW + lumaSE) * (0.25 * FXAA_REDUCE_MUL), FXAA_REDUCE_MIN);

    // Calculate the reciprocal and scale the direction
    float rcpDirMin = 1.0 / (min(abs(dir.x), abs(dir.y)) + dirReduce);
    dir = min(float2(FXAA_SPAN_MAX, FXAA_SPAN_MAX),
              max(float2(-FXAA_SPAN_MAX, -FXAA_SPAN_MAX),
                  dir * rcpDirMin)) * texelSize;

    // Sample along the calculated direction
    float3 rgbA = 0.5 * (
        SAMPLE_TEX2D(_texture, uv + dir * (1.0 / 3.0 - 0.5)).rgb +
                         SAMPLE_TEX2D(_texture, uv + dir * (2.0 / 3.0 - 0.5)).rgb);

    float3 rgbB = rgbA * 0.5 + 0.25 * (
        SAMPLE_TEX2D(_texture, uv + dir * (0.0 / 3.0 - 0.5)).rgb +
                                       SAMPLE_TEX2D(_texture, uv + dir * (3.0 / 3.0 - 0.5)).rgb);

    float lumaB = dot(rgbB, luma);

    // Choose the appropriate result
    if ((lumaB < lumaMin) || (lumaB > lumaMax)) {
        return rgbA;
    } else {
        return rgbB;
    }
}

[shader("pixel")]
float4 MainPS(V2F input) : SV_TARGET {
    float3 color = FXAA(input.uv);
    return float4(color, 1.0);
} 