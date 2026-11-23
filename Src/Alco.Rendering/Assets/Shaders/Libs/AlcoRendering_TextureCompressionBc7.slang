// file "AlcoRendering_TextureCompressionBc7.slang" - module "AlcoRendering_TextureCompressionBc7"
//
// BC7 mode 6 and mode 5 endpoint fitting, decoded-error scoring, and block packing.

#language slang 2025
module AlcoRendering_TextureCompressionBc7;

static const uint Bc7Weights2[4] = { 0u, 21u, 43u, 64u };
static const uint Bc7Weights4[16] = { 0u, 4u, 9u, 13u, 17u, 21u, 26u, 30u, 34u, 38u, 43u, 47u, 51u, 55u, 60u, 64u };

void Bc7WriteBits(inout uint4 block, inout uint position, uint value, uint count)
{
    uint word = position >> 5u;
    uint shift = position & 31u;
    value &= (1u << count) - 1u;
    block[word] |= value << shift;
    if (shift + count > 32u)
        block[word + 1u] |= value >> (32u - shift);
    position += count;
}

float4 Bc7Rotate(float4 pixel, uint rotation)
{
    if (rotation != 0u)
    {
        float scalar = pixel[rotation - 1u];
        pixel[rotation - 1u] = pixel.a;
        pixel.a = scalar;
    }
    return pixel;
}

void Bc7InitializeEndpoints(float4 pixels[16], uint rotation, bool separateAlpha, out float4 endpoint0, out float4 endpoint1)
{
    float4 mean = 0.0f;
    float minimumAlpha = 255.0f;
    float maximumAlpha = 0.0f;
    [loop]
    for (uint i = 0u; i < 16u; ++i)
    {
        float4 pixel = Bc7Rotate(pixels[i], rotation);
        mean += pixel;
        minimumAlpha = min(minimumAlpha, pixel.a);
        maximumAlpha = max(maximumAlpha, pixel.a);
    }
    mean *= 1.0f / 16.0f;

    float4 covarianceR = 0.0f;
    float4 covarianceG = 0.0f;
    float4 covarianceB = 0.0f;
    float4 covarianceA = 0.0f;
    [loop]
    for (uint i = 0u; i < 16u; ++i)
    {
        float4 delta = Bc7Rotate(pixels[i], rotation) - mean;
        if (separateAlpha)
            delta.a = 0.0f;
        covarianceR += delta.r * delta;
        covarianceG += delta.g * delta;
        covarianceB += delta.b * delta;
        covarianceA += delta.a * delta;
    }

    float4 variance = float4(covarianceR.r, covarianceG.g, covarianceB.b, covarianceA.a);
    uint component = 0u;
    for (uint i = 1u; i < 4u; ++i)
        if (variance[i] > variance[component])
            component = i;
    float4 axis = 0.0f;
    axis[component] = 1.0f;
    for (uint iteration = 0u; iteration < 6u; ++iteration)
    {
        float4 next = float4(dot(covarianceR, axis), dot(covarianceG, axis), dot(covarianceB, axis), dot(covarianceA, axis));
        float lengthSquared = dot(next, next);
        if (lengthSquared <= 1e-12f)
            break;
        axis = next * rsqrt(lengthSquared);
    }

    float minimumProjection = 1e30f;
    float maximumProjection = -1e30f;
    [loop]
    for (uint i = 0u; i < 16u; ++i)
    {
        float4 delta = Bc7Rotate(pixels[i], rotation) - mean;
        if (separateAlpha)
            delta.a = 0.0f;
        float projection = dot(delta, axis);
        minimumProjection = min(minimumProjection, projection);
        maximumProjection = max(maximumProjection, projection);
    }
    endpoint0 = clamp(mean + minimumProjection * axis, 0.0f, 255.0f);
    endpoint1 = clamp(mean + maximumProjection * axis, 0.0f, 255.0f);
    if (separateAlpha)
    {
        endpoint0.a = minimumAlpha;
        endpoint1.a = maximumAlpha;
    }
}

void Bc7RefineEndpoints(float4 pixels[16], uint2 indices[16], uint rotation, bool mode6, out float4 endpoint0, out float4 endpoint1)
{
    float4 sumAA = 0.0f;
    float4 sumAB = 0.0f;
    float4 sumBB = 0.0f;
    float4 sumAX = 0.0f;
    float4 sumBX = 0.0f;
    float4 mean = 0.0f;
    [loop]
    for (uint i = 0u; i < 16u; ++i)
    {
        uint2 paletteIndices = mode6 ? indices[i].xx : indices[i] * 5u;
        float colorWeight = float(Bc7Weights4[paletteIndices.x]) / 64.0f;
        float alphaWeight = float(Bc7Weights4[paletteIndices.y]) / 64.0f;
        float4 weightB = float4(colorWeight, colorWeight, colorWeight, alphaWeight);
        float4 weightA = 1.0f - weightB;
        float4 pixel = Bc7Rotate(pixels[i], rotation);
        sumAA += weightA * weightA;
        sumAB += weightA * weightB;
        sumBB += weightB * weightB;
        sumAX += weightA * pixel;
        sumBX += weightB * pixel;
        mean += pixel;
    }
    float4 determinant = sumAA * sumBB - sumAB * sumAB;
    endpoint0 = endpoint1 = mean / 16.0f;
    for (uint component = 0u; component < 4u; ++component)
    {
        if (determinant[component] > 1e-6f)
        {
            endpoint0[component] = (sumAX[component] * sumBB[component] - sumBX[component] * sumAB[component]) / determinant[component];
            endpoint1[component] = (sumBX[component] * sumAA[component] - sumAX[component] * sumAB[component]) / determinant[component];
        }
    }
    endpoint0 = clamp(endpoint0, 0.0f, 255.0f);
    endpoint1 = clamp(endpoint1, 0.0f, 255.0f);
}

uint4 Bc7QuantizeMode6(float4 endpoint, uint pbit)
{
    uint4 quantized = uint4(clamp(floor((endpoint - float(pbit)) * 0.5f + 0.5f), 0.0f, 127.0f));
    return (quantized << 1u) | pbit;
}

uint4 Bc7QuantizeMode5(float4 endpoint)
{
    endpoint = clamp(endpoint, 0.0f, 255.0f);
    float3 lower = min(floor(endpoint.rgb * 0.5f + 0.5f), 63.0f);
    float3 upper = max(floor((endpoint.rgb - 1.0f) * 0.5f + 0.5f), 64.0f);
    uint3 quantized = uint3(lerp(lower, upper, step(127.5f, endpoint.rgb)));
    return uint4((quantized << 1u) | (quantized >> 6u), uint(floor(endpoint.a + 0.5f)));
}

float Bc7EvaluateMode6(float4 pixels[16], uint4 endpoint0, uint4 endpoint1, out uint2 indices[16])
{
    float error = 0.0f;
    [loop]
    for (uint i = 0u; i < 16u; ++i)
    {
        float minimumError = 1e30f;
        uint bestIndex = 0u;
        [loop]
        for (uint index = 0u; index < 16u; ++index)
        {
            uint weight = Bc7Weights4[index];
            uint4 decoded = ((64u - weight) * endpoint0 + weight * endpoint1 + 32u) >> 6u;
            float4 delta = pixels[i] - float4(decoded);
            float candidateError = dot(delta, delta);
            if (candidateError < minimumError)
            {
                minimumError = candidateError;
                bestIndex = index;
            }
        }
        indices[i] = uint2(bestIndex, bestIndex);
        error += minimumError;
    }
    return error;
}

float Bc7EvaluateMode5(float4 pixels[16], uint rotation, uint4 endpoint0, uint4 endpoint1, out uint2 indices[16])
{
    float error = 0.0f;
    [loop]
    for (uint i = 0u; i < 16u; ++i)
    {
        float4 pixel = Bc7Rotate(pixels[i], rotation);
        float2 minimumError = 1e30f;
        uint2 bestIndex = 0u;
        for (uint index = 0u; index < 4u; ++index)
        {
            uint weight = Bc7Weights2[index];
            uint4 decoded = ((64u - weight) * endpoint0 + weight * endpoint1 + 32u) >> 6u;
            float4 delta = pixel - float4(decoded);
            float2 candidateError = float2(dot(delta.rgb, delta.rgb), delta.a * delta.a);
            if (candidateError.x < minimumError.x)
            {
                minimumError.x = candidateError.x;
                bestIndex.x = index;
            }
            if (candidateError.y < minimumError.y)
            {
                minimumError.y = candidateError.y;
                bestIndex.y = index;
            }
        }
        indices[i] = bestIndex;
        error += minimumError.x + minimumError.y;
    }
    return error;
}

uint4 Bc7PackMode6(uint4 endpoint0, uint4 endpoint1, uint2 indices[16])
{
    bool reverse = indices[0].x >= 8u;
    if (reverse)
    {
        uint4 temporary = endpoint0;
        endpoint0 = endpoint1;
        endpoint1 = temporary;
    }
    uint4 block = 0u;
    uint position = 0u;
    Bc7WriteBits(block, position, 0x40u, 7u);
    for (uint component = 0u; component < 4u; ++component)
    {
        Bc7WriteBits(block, position, endpoint0[component] >> 1u, 7u);
        Bc7WriteBits(block, position, endpoint1[component] >> 1u, 7u);
    }
    Bc7WriteBits(block, position, endpoint0.r & 1u, 1u);
    Bc7WriteBits(block, position, endpoint1.r & 1u, 1u);
    for (uint i = 0u; i < 16u; ++i)
        Bc7WriteBits(block, position, reverse ? 15u - indices[i].x : indices[i].x, i == 0u ? 3u : 4u);
    return block;
}

uint4 Bc7PackMode5(uint4 endpoint0, uint4 endpoint1, uint2 indices[16], uint rotation)
{
    bool reverseColor = indices[0].x >= 2u;
    bool reverseAlpha = indices[0].y >= 2u;
    if (reverseColor)
    {
        uint3 temporary = endpoint0.rgb;
        endpoint0.rgb = endpoint1.rgb;
        endpoint1.rgb = temporary;
    }
    if (reverseAlpha)
    {
        uint temporary = endpoint0.a;
        endpoint0.a = endpoint1.a;
        endpoint1.a = temporary;
    }
    uint4 block = 0u;
    uint position = 0u;
    Bc7WriteBits(block, position, 0x20u, 6u);
    Bc7WriteBits(block, position, rotation, 2u);
    for (uint component = 0u; component < 3u; ++component)
    {
        Bc7WriteBits(block, position, endpoint0[component] >> 1u, 7u);
        Bc7WriteBits(block, position, endpoint1[component] >> 1u, 7u);
    }
    Bc7WriteBits(block, position, endpoint0.a, 8u);
    Bc7WriteBits(block, position, endpoint1.a, 8u);
    for (uint i = 0u; i < 16u; ++i)
        Bc7WriteBits(block, position, reverseColor ? 3u - indices[i].x : indices[i].x, i == 0u ? 1u : 2u);
    for (uint i = 0u; i < 16u; ++i)
        Bc7WriteBits(block, position, reverseAlpha ? 3u - indices[i].y : indices[i].y, i == 0u ? 1u : 2u);
    return block;
}

public uint4 CompressBc7(float4 pixels[16])
{
    bool zeroAlpha = true;
    bool opaqueAlpha = true;
    [loop]
    for (uint i = 0u; i < 16u; ++i)
    {
        pixels[i] = saturate(pixels[i]) * 255.0f;
        zeroAlpha = zeroAlpha && pixels[i].a == 0.0f;
        opaqueAlpha = opaqueAlpha && pixels[i].a == 255.0f;
    }

    uint4 bestBlock = 0u;
    float bestError = 1e30f;
    float4 initial0;
    float4 initial1;
    Bc7InitializeEndpoints(pixels, 0u, false, initial0, initial1);
    [loop]
    for (uint pbits = 0u; pbits < 4u; ++pbits)
    {
        if ((zeroAlpha && pbits != 0u) || (opaqueAlpha && pbits != 3u))
            continue;
        float4 endpoint0 = initial0;
        float4 endpoint1 = initial1;
        [loop]
        for (uint iteration = 0u; iteration < 3u; ++iteration)
        {
            uint4 decoded0 = Bc7QuantizeMode6(endpoint0, pbits & 1u);
            uint4 decoded1 = Bc7QuantizeMode6(endpoint1, pbits >> 1u);
            uint2 indices[16];
            float error = Bc7EvaluateMode6(pixels, decoded0, decoded1, indices);
            if (error < bestError)
            {
                bestError = error;
                bestBlock = Bc7PackMode6(decoded0, decoded1, indices);
            }
            if (bestError == 0.0f)
                return bestBlock;
            if (iteration < 2u)
                Bc7RefineEndpoints(pixels, indices, 0u, true, endpoint0, endpoint1);
        }
    }

    [loop]
    for (uint rotation = 0u; rotation < 4u; ++rotation)
    {
        float4 endpoint0;
        float4 endpoint1;
        Bc7InitializeEndpoints(pixels, rotation, true, endpoint0, endpoint1);
        [loop]
        for (uint iteration = 0u; iteration < 3u; ++iteration)
        {
            uint4 decoded0 = Bc7QuantizeMode5(endpoint0);
            uint4 decoded1 = Bc7QuantizeMode5(endpoint1);
            uint2 indices[16];
            float error = Bc7EvaluateMode5(pixels, rotation, decoded0, decoded1, indices);
            if (error < bestError)
            {
                bestError = error;
                bestBlock = Bc7PackMode5(decoded0, decoded1, indices, rotation);
            }
            if (bestError == 0.0f)
                return bestBlock;
            if (iteration < 2u)
                Bc7RefineEndpoints(pixels, indices, rotation, false, endpoint0, endpoint1);
        }
    }
    return bestBlock;
}
