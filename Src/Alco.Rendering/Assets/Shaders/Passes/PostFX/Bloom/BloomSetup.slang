// file "BloomSetup.slang" - module "BloomSetup"
//
// Resources are grouped as ParameterBlocks — the compiler assigns each block
// its own descriptor set in declaration order; members resolve by bare field name.

#language slang 2025
module BloomSetup;

import AlcoRendering_Core;

struct Constants
{
    float threshold;    // brightness cutoff; below 0 the threshold is disabled (whole image blooms)
};

public struct PassParams
{
    public Texture2D texture;
};

public ParameterBlock<PassParams> pass;
[[vk::push_constant]] Constants constants;

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

[shader("vertex")]
V2F MainVS(Vertex input)
{
    V2F output = (V2F)0;
    output.position = float4(input.position, 1.0f);
    output.uv = input.uv;
    return output;
}

// Bloom setup: extract the light contributing to bloom. The threshold is a
// linear ramp reaching full strength two units above the cutoff; a negative
// threshold disables it so the whole image blooms.
[shader("fragment")]
float4 MainPS(V2F input)
    : SV_TARGET
{
    float4 color = pass.texture.Sample(samplers.linearClamp, input.uv);
    if (any(isnan(color.rgb)) || any(isinf(color.rgb)))
    {
        return float4(0, 0, 0, 1);
    }

    float3 linearColor = max(color.rgb, 0.0);
    float amount = 1.0;
    if (constants.threshold >= 0.0)
    {
        float luma = dot(linearColor, float3(0.3, 0.59, 0.11));
        amount = saturate((luma - constants.threshold) * 0.5);
    }

    return float4(linearColor * amount, 1.0);
}
