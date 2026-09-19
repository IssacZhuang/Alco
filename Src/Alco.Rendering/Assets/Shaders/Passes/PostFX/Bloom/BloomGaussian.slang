// file "BloomGaussian.slang" - module "BloomGaussian"
//
// Resources are grouped as ParameterBlocks — the compiler assigns each block
// its own descriptor set in declaration order; members resolve by bare field name.

#language slang 2025
module BloomGaussian;

import AlcoRendering_Core;

// One pass of the separable bloom gaussian: weights follow
// exp(-16.7 * (x / radius)^2), taps are paired two-by-two into single bilinear
// samples, and the kernel is normalized to unit energy. The vertical pass
// additionally folds in the stage tint and adds the previous, coarser
// accumulation bilinearly.
struct Constants {
    float2 direction;     // (1,0) horizontal pass, (0,1) vertical pass
    float sigmaRadius;    // gaussian denominator: the clamped float radius in texels
    int integerRadius;    // loop bound: ceil(sigmaRadius), clamped to [1, 31]
    float tintR;          // vertical-pass tint (stage tint * intensity / stage count)
    float tintG;
    float tintB;
    float additiveBlend;  // 1.0 adds the coarser accumulation texture
    float2 invSourceSize; // 1 / source extent, for texel offsets
};

public struct SourceParams
{
    public Texture2D source;
};

public ParameterBlock<SourceParams> sourceBlock;

public struct AdditiveParams
{
    public Texture2D additive;
};

public ParameterBlock<AdditiveParams> additiveBlock;

[[vk::push_constant]] Constants constants;

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

float gaussianWeight(float x)
{
    return exp(-16.7 * (x / constants.sigmaRadius) * (x / constants.sigmaRadius));
}

[shader("fragment")]
float4 MainPS(V2F input) : SV_TARGET {
    float3 sum = float3(0, 0, 0);
    float weightSum = 0.0;

    for (int i = -constants.integerRadius; i <= constants.integerRadius; i += 2) {
        float weight0 = gaussianWeight(float(i));
        float weight1 = 0.0;

        // The bilinear pairing merges two kernel taps into one sample; the last
        // tap stays on its pixel center so the kernel is not biased off-window.
        if (i != constants.integerRadius) {
            weight1 = gaussianWeight(float(i) + 1.0);
        }

        float totalWeight = weight0 + weight1;
        float offset = float(i) + weight1 / totalWeight;
        weightSum += totalWeight;

        float2 uv = input.uv + constants.direction * (offset * constants.invSourceSize);
        sum += totalWeight * sourceBlock.source.Sample(samplers.linearClamp, uv).rgb;
    }

    float3 color = sum / weightSum;
    color *= float3(constants.tintR, constants.tintG, constants.tintB);

    if (constants.additiveBlend > 0.5) {
        color += additiveBlock.additive.Sample(samplers.linearClamp, input.uv).rgb;
    }

    return float4(color, 1.0);
}
