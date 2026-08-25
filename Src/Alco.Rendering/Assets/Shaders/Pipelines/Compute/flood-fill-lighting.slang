// file "flood-fill-lighting.slang" - module "flood_fill_lighting"
//
// Slang module converted from Shaders/Pipelines/Compute/FloodFillLighting.hlsl (Phase 2 migration).
// Resources are grouped as ParameterBlocks — the compiler assigns each block
// its own descriptor set in declaration order; members resolve by bare field name.

#language slang 2025
module flood_fill_lighting;

import alco_rendering_core;

struct Constants {
    float attenuationSide;
    float attenuationCorner;
};

// light map texture
public struct PassParams
{
    public [[vk::image_format("rgba16f")]] RWTexture2D<float4> _frontBuffer;
};

public ParameterBlock<PassParams> _pass;
public struct BackBufferParams
{
    public [[vk::image_format("rgba16f")]] RWTexture2D<float4> _backBuffer;
};

public ParameterBlock<BackBufferParams> _backBuffer;

// opacity map texture
public struct OpacityMapParams
{
    public [[vk::image_format("rgba8")]] RWTexture2D<float4> _opacityMap;
};

public ParameterBlock<OpacityMapParams> _opacityMap;

[[vk::push_constant]] Constants constants;


float GetLightPassingFactor(uint2 pos) {
    //todo: the passing factor of wall is 0
    return 1;
}

// float4 AdjustLuminance(float4 color, float attenuation) {
//     float luminance = dot(color.rgb, float3(0.2126, 0.7152, 0.0722));
//     float newLuminance = max(0, luminance - attenuation);

//     // Avoid division by zero and improve numerical stability
//     if (luminance < 1e-6) {
//         return float4(0, 0, 0, color.a);
//     }

//     float scale = newLuminance / luminance;
//     return float4(color.rgb * scale, color.a);
// }

// Alternative 1: Exponential decay (more physically accurate)
float4 AdjustLuminanceExponential(float4 color, float attenuation) {
    float luminance = dot(color.rgb, float3(0.2126, 0.7152, 0.0722));

    if (luminance < 1e-6) {
        return float4(0, 0, 0, color.a);
    }

    // Exponential decay: I = I0 * e^(-attenuation)
    float scale = exp(-attenuation);
    return float4(color.rgb * scale, color.a);
}


[shader("compute")]
[numthreads(16, 16, 1)]
void MainCS(uint3 id: SV_DispatchThreadID) {
    uint2 pos = id.xy;

    float4 colors[8] = {
        _pass._frontBuffer[id.xy + int2(1, 0)],
        _pass._frontBuffer[id.xy + int2(-1, 0)],
        _pass._frontBuffer[id.xy + int2(0, 1)],
        _pass._frontBuffer[id.xy + int2(0, -1)],
        _pass._frontBuffer[id.xy + int2(1, 1)],
        _pass._frontBuffer[id.xy + int2(-1, 1)],
        _pass._frontBuffer[id.xy + int2(1, -1)],
        _pass._frontBuffer[id.xy + int2(-1, -1)],
    };

    float attenuationSide = constants.attenuationSide;
    float attenuationCorner = constants.attenuationCorner;

    float4 color = _pass._frontBuffer[id.xy];
    color = max(color, AdjustLuminanceExponential(colors[0], attenuationSide));
    color = max(color, AdjustLuminanceExponential(colors[1], attenuationSide));
    color = max(color, AdjustLuminanceExponential(colors[2], attenuationSide));
    color = max(color, AdjustLuminanceExponential(colors[3], attenuationSide));
    color = max(color, AdjustLuminanceExponential(colors[4], attenuationCorner));
    color = max(color, AdjustLuminanceExponential(colors[5], attenuationCorner));
    color = max(color, AdjustLuminanceExponential(colors[6], attenuationCorner));
    color = max(color, AdjustLuminanceExponential(colors[7], attenuationCorner));

    float4 opacity = _opacityMap._opacityMap[id.xy];
    color = color * opacity;

    _backBuffer._backBuffer[id.xy] = color * GetLightPassingFactor(id.xy);
}







