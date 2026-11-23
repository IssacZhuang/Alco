// file "AlcoRendering_TextureCompressionLegacy.slang" - module "AlcoRendering_TextureCompressionLegacy"
//
// BC1-BC5 block encoders with PCA axis fitting, least-squares refinement, and 32-bit packing.

#language slang 2025
module AlcoRendering_TextureCompressionLegacy;

uint LegacyBcQuantize565(float3 color)
{
    uint3 quantized = uint3(floor(saturate(color) * float3(31.0f, 63.0f, 31.0f) + 0.5f));
    return (quantized.r << 11u) | (quantized.g << 5u) | quantized.b;
}

float3 LegacyBcDecode565(uint color)
{
    uint red = (color >> 11u) & 31u;
    uint green = (color >> 5u) & 63u;
    uint blue = color & 31u;
    return float3((red << 3u) | (red >> 2u), (green << 2u) | (green >> 4u), (blue << 3u) | (blue >> 2u)) * (1.0f / 255.0f);
}

uint2 LegacyBcPackColor(float4 pixels[16], float3 firstColor, float3 secondColor, bool transparentMode, out float error)
{
    uint endpoint0 = LegacyBcQuantize565(firstColor);
    uint endpoint1 = LegacyBcQuantize565(secondColor);
    if ((transparentMode && endpoint0 > endpoint1) || (!transparentMode && endpoint0 < endpoint1))
    {
        uint temporary = endpoint0;
        endpoint0 = endpoint1;
        endpoint1 = temporary;
    }
    if (!transparentMode && endpoint0 == endpoint1)
    {
        if (endpoint0 < 65535u)
            endpoint0 += 1u;
        else
            endpoint1 -= 1u;
    }

    float3 palette[4];
    palette[0] = LegacyBcDecode565(endpoint0);
    palette[1] = LegacyBcDecode565(endpoint1);
    palette[2] = transparentMode ? (palette[0] + palette[1]) * 0.5f : (2.0f * palette[0] + palette[1]) * (1.0f / 3.0f);
    palette[3] = transparentMode ? float3(0.0f) : (palette[0] + 2.0f * palette[1]) * (1.0f / 3.0f);

    uint indices = 0u;
    error = 0.0f;
    [unroll]
    for (uint pixelIndex = 0u; pixelIndex < 16u; pixelIndex++)
    {
        uint nearestIndex = 3u;
        if (!transparentMode || pixels[pixelIndex].a >= 0.5f)
        {
            float3 color = saturate(pixels[pixelIndex].rgb);
            float3 difference = color - palette[0];
            float nearestError = dot(difference, difference);
            nearestIndex = 0u;
            [unroll]
            for (uint paletteIndex = 1u; paletteIndex < 4u; paletteIndex++)
            {
                if (!transparentMode || paletteIndex < 3u)
                {
                    difference = color - palette[paletteIndex];
                    float candidateError = dot(difference, difference);
                    if (candidateError < nearestError)
                    {
                        nearestError = candidateError;
                        nearestIndex = paletteIndex;
                    }
                }
            }
            error += nearestError;
        }
        indices |= nearestIndex << (2u * pixelIndex);
    }
    return uint2(endpoint0 | (endpoint1 << 16u), indices);
}

uint2 LegacyBcCompressColor(float4 pixels[16], bool oneBitAlpha)
{
    float3 minimumColor = float3(1.0f);
    float3 maximumColor = float3(0.0f);
    float3 mean = float3(0.0f);
    uint colorCount = 0u;
    bool transparentMode = false;
    [unroll]
    for (uint pixelIndex = 0u; pixelIndex < 16u; pixelIndex++)
    {
        if (oneBitAlpha && pixels[pixelIndex].a < 0.5f)
        {
            transparentMode = true;
            continue;
        }
        float3 color = saturate(pixels[pixelIndex].rgb);
        minimumColor = min(minimumColor, color);
        maximumColor = max(maximumColor, color);
        mean += color;
        colorCount++;
    }
    if (colorCount == 0u)
        return uint2(0u, 0xffffffffu);
    mean /= float(colorCount);

    float3 covariance0 = float3(0.0f);
    float3 covariance1 = float3(0.0f);
    float3 covariance2 = float3(0.0f);
    [unroll]
    for (uint pixelIndex = 0u; pixelIndex < 16u; pixelIndex++)
    {
        if (transparentMode && pixels[pixelIndex].a < 0.5f)
            continue;
        float3 centered = saturate(pixels[pixelIndex].rgb) - mean;
        covariance0 += centered.x * centered;
        covariance1 += centered.y * centered;
        covariance2 += centered.z * centered;
    }

    float3 axis = covariance0;
    float largestVariance = covariance0.x;
    if (covariance1.y > largestVariance)
    {
        axis = covariance1;
        largestVariance = covariance1.y;
    }
    if (covariance2.z > largestVariance)
        axis = covariance2;

    float3 firstColor = maximumColor;
    float3 secondColor = minimumColor;
    if (dot(axis, axis) > 1e-12f)
    {
        axis = normalize(axis);
        [unroll]
        for (uint iteration = 0u; iteration < 4u; iteration++)
        {
            float3 nextAxis = float3(dot(covariance0, axis), dot(covariance1, axis), dot(covariance2, axis));
            float lengthSquared = dot(nextAxis, nextAxis);
            if (lengthSquared > 1e-20f)
                axis = nextAxis * rsqrt(lengthSquared);
        }
        float minimumProjection = 1e20f;
        float maximumProjection = -1e20f;
        [unroll]
        for (uint pixelIndex = 0u; pixelIndex < 16u; pixelIndex++)
        {
            if (transparentMode && pixels[pixelIndex].a < 0.5f)
                continue;
            float projection = dot(saturate(pixels[pixelIndex].rgb) - mean, axis);
            minimumProjection = min(minimumProjection, projection);
            maximumProjection = max(maximumProjection, projection);
        }
        firstColor = mean + axis * maximumProjection;
        secondColor = mean + axis * minimumProjection;
    }

    float bestError;
    uint2 best = LegacyBcPackColor(pixels, firstColor, secondColor, transparentMode, bestError);
    if (bestError == 0.0f)
        return best;

    float weight00 = 0.0f;
    float weight01 = 0.0f;
    float weight11 = 0.0f;
    float3 weightedColor0 = float3(0.0f);
    float3 weightedColor1 = float3(0.0f);
    [unroll]
    for (uint pixelIndex = 0u; pixelIndex < 16u; pixelIndex++)
    {
        uint index = (best.y >> (2u * pixelIndex)) & 3u;
        if (transparentMode && index == 3u)
            continue;
        float weight0 = index == 0u ? 1.0f : index == 1u ? 0.0f : index == 2u ? (transparentMode ? 0.5f : 2.0f / 3.0f) : 1.0f / 3.0f;
        float weight1 = 1.0f - weight0;
        float3 color = saturate(pixels[pixelIndex].rgb);
        weight00 += weight0 * weight0;
        weight01 += weight0 * weight1;
        weight11 += weight1 * weight1;
        weightedColor0 += weight0 * color;
        weightedColor1 += weight1 * color;
    }
    float determinant = weight00 * weight11 - weight01 * weight01;
    if (determinant > 1e-6f)
    {
        firstColor = (weightedColor0 * weight11 - weightedColor1 * weight01) / determinant;
        secondColor = (weightedColor1 * weight00 - weightedColor0 * weight01) / determinant;
        float candidateError;
        uint2 candidate = LegacyBcPackColor(pixels, firstColor, secondColor, transparentMode, candidateError);
        if (candidateError < bestError)
            best = candidate;
    }
    return best;
}

uint2 LegacyBcPackChannel(float values[16], int endpoint0, int endpoint1, bool signedValues, out float error)
{
    float inverseScale = signedValues ? 1.0f / 127.0f : 1.0f / 255.0f;
    float palette[8];
    palette[0] = float(endpoint0) * inverseScale;
    palette[1] = float(endpoint1) * inverseScale;
    if (endpoint0 > endpoint1)
    {
        [unroll]
        for (uint paletteIndex = 2u; paletteIndex < 8u; paletteIndex++)
            palette[paletteIndex] = ((8.0f - float(paletteIndex)) * palette[0] + (float(paletteIndex) - 1.0f) * palette[1]) * (1.0f / 7.0f);
    }
    else
    {
        [unroll]
        for (uint paletteIndex = 2u; paletteIndex < 6u; paletteIndex++)
            palette[paletteIndex] = ((6.0f - float(paletteIndex)) * palette[0] + (float(paletteIndex) - 1.0f) * palette[1]) * (1.0f / 5.0f);
        palette[6] = signedValues ? -1.0f : 0.0f;
        palette[7] = 1.0f;
    }

    uint2 block = uint2((uint(endpoint0) & 255u) | ((uint(endpoint1) & 255u) << 8u), 0u);
    error = 0.0f;
    [unroll]
    for (uint pixelIndex = 0u; pixelIndex < 16u; pixelIndex++)
    {
        uint nearestIndex = 0u;
        float difference = values[pixelIndex] - palette[0];
        float nearestError = difference * difference;
        [unroll]
        for (uint paletteIndex = 1u; paletteIndex < 8u; paletteIndex++)
        {
            difference = values[pixelIndex] - palette[paletteIndex];
            float candidateError = difference * difference;
            if (candidateError < nearestError)
            {
                nearestError = candidateError;
                nearestIndex = paletteIndex;
            }
        }
        error += nearestError;
        uint bitOffset = 16u + 3u * pixelIndex;
        if (bitOffset < 32u)
        {
            block.x |= nearestIndex << bitOffset;
            if (bitOffset > 29u)
                block.y |= nearestIndex >> (32u - bitOffset);
        }
        else
            block.y |= nearestIndex << (bitOffset - 32u);
    }
    return block;
}

uint2 LegacyBcCompressChannel(float4 pixels[16], uint channel, bool signedValues)
{
    float lowerBound = signedValues ? -1.0f : 0.0f;
    float scale = signedValues ? 127.0f : 255.0f;
    float minimumValue = 1.0f;
    float maximumValue = lowerBound;
    float interiorMinimum = 1.0f;
    float interiorMaximum = lowerBound;
    uint interiorCount = 0u;
    float values[16];
    [unroll]
    for (uint pixelIndex = 0u; pixelIndex < 16u; pixelIndex++)
    {
        float value = clamp(pixels[pixelIndex][channel], lowerBound, 1.0f);
        values[pixelIndex] = value;
        minimumValue = min(minimumValue, value);
        maximumValue = max(maximumValue, value);
        if (value > lowerBound + 0.5f / scale && value < 1.0f - 0.5f / scale)
        {
            interiorMinimum = min(interiorMinimum, value);
            interiorMaximum = max(interiorMaximum, value);
            interiorCount++;
        }
    }

    // -127 encodes SNORM -1 without the duplicate -128 endpoint's ordering ambiguity.
    int minimumEndpoint = int(floor(minimumValue * scale + 0.5f));
    int maximumEndpoint = int(floor(maximumValue * scale + 0.5f));
    if (minimumEndpoint == maximumEndpoint)
    {
        uint endpointByte = uint(minimumEndpoint) & 255u;
        return uint2(endpointByte | (endpointByte << 8u), 0u);
    }

    float bestError;
    uint2 best = LegacyBcPackChannel(values, maximumEndpoint, minimumEndpoint, signedValues, bestError);
    if (bestError == 0.0f)
        return best;

    int firstEndpoint = interiorCount == 0u ? minimumEndpoint : int(floor(interiorMinimum * scale + 0.5f));
    int secondEndpoint = interiorCount == 0u ? maximumEndpoint : int(floor(interiorMaximum * scale + 0.5f));
    float candidateError;
    uint2 candidate = LegacyBcPackChannel(values, firstEndpoint, secondEndpoint, signedValues, candidateError);
    if (candidateError < bestError)
        best = candidate;
    return best;
}

public uint2 CompressBc1(float4 pixels[16])
{
    return LegacyBcCompressColor(pixels, true);
}

public uint4 CompressBc2(float4 pixels[16])
{
    uint2 alpha = uint2(0u);
    [unroll]
    for (uint pixelIndex = 0u; pixelIndex < 8u; pixelIndex++)
    {
        alpha.x |= uint(floor(saturate(pixels[pixelIndex].a) * 15.0f + 0.5f)) << (4u * pixelIndex);
        alpha.y |= uint(floor(saturate(pixels[pixelIndex + 8u].a) * 15.0f + 0.5f)) << (4u * pixelIndex);
    }
    uint2 color = LegacyBcCompressColor(pixels, false);
    return uint4(alpha, color);
}

public uint4 CompressBc3(float4 pixels[16])
{
    uint2 alpha = LegacyBcCompressChannel(pixels, 3u, false);
    uint2 color = LegacyBcCompressColor(pixels, false);
    return uint4(alpha, color);
}

public uint2 CompressBc4(float4 pixels[16], bool signedValues)
{
    return LegacyBcCompressChannel(pixels, 0u, signedValues);
}

public uint4 CompressBc5(float4 pixels[16])
{
    uint2 red = LegacyBcCompressChannel(pixels, 0u, false);
    uint2 green = LegacyBcCompressChannel(pixels, 1u, false);
    return uint4(red, green);
}
