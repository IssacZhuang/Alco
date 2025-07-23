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
    float luma = dot(color.rgb, float3(0.2126, 0.7152, 0.0722));
    return color * max(0, luma - constants.Threshold) / max(luma, 0.00001);
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
    float2 invTextureSize = constants.InvTextureSize;
    float4 sum = float4(0, 0, 0, 0);
    float weights[5] = { 0.07027, 0.316216, 0.227027, 0.316216,
                         0.07027 }; // Gaussian weights for a 5x5 kernel

    // Apply the weights from the Gaussian kernel
    for (int i = -2; i <= 2; ++i)
    {
        for (int j = -2; j <= 2; ++j)
        {
            float weight = weights[i + 2] * weights[j + 2];
            sum += weight * SampleTextureClamped(input.uv + float2(i, j) * invTextureSize);
        }
    }

    const float IntensityBase = 2;

    return float4(sum.rgb * constants.Spread, sum.a) * constants.Intensity * IntensityBase;
}
