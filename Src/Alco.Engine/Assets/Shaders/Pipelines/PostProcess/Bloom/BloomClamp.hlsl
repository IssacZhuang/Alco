#include "Shaders/Libs/Core.hlsli"

struct Constants
{
    float2 InvTextureSize;
    float Threshold;
    float Spread;
    float Intensity;
};

DEFINE_TEX2D_SAMPLE(0, _texture);
PUSH_CONSTANT Constants constants;

struct Vertex
{
    float3 position : POSITION;
    float2 uv : TEXCOORD0;
};

struct V2F
{
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
};

float4 SampleTextureClamped(float2 uv)
{
    float4 color = SAMPLE_TEX2D(_texture, uv);
    if (any(isnan(color)) || any(isinf(color)))
    {
        return float4(0, 0, 0, 0);
    }

    // Extract the radiance above the threshold without multiplying it by the
    // source radiance. The old color * (brightness - threshold) response grew
    // quadratically and turned very bright sources into solid bloom volumes.
    float brightness = max(max(color.r, color.g), color.b);
    float knee = max(constants.Threshold * 0.5, 0.0001);
    float soft = clamp(brightness - constants.Threshold + knee, 0.0, 2.0 * knee);
    soft = soft * soft / (4.0 * knee);
    float contribution = max(soft, brightness - constants.Threshold) / max(brightness, 0.0001);
    return float4(max(color.rgb, 0.0) * contribution, 1.0);
}

[shader("vertex")]
V2F MainVS(Vertex input)
{
    V2F output = (V2F)0;
    output.position = float4(input.position, 1.0f);
    output.uv = input.uv;
    return output;
}

// clamp and then calc gaussian blur once
[shader("pixel")]
float4 MainPS(V2F input)
    : SV_TARGET
{
    float2 sampleOffset = constants.InvTextureSize * constants.Spread;
    float4 sum = float4(0, 0, 0, 0);
    float weights[5] = { 0.06136, 0.24477, 0.38774, 0.24477,
                         0.06136 }; // Normalized Gaussian weights for a 5x5 kernel

    // Apply the weights from the Gaussian kernel
    for (int i = -2; i <= 2; ++i)
    {
        for (int j = -2; j <= 2; ++j)
        {
            float weight = weights[i + 2] * weights[j + 2];
            sum += weight * SampleTextureClamped(input.uv + float2(i, j) * sampleOffset);
        }
    }

    // Intensity is applied exactly once. The 2x display scale preserves the
    // established control range while the extracted radiance stays linear.
    // Spread changes only the blur footprint, never its energy.
    return float4(sum.rgb * constants.Intensity, 1.0);
}
