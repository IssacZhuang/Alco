//translate GLSL code from https://www.shadertoy.com/view/flt3Wn to HLSL

// Helper function: hash22 - generates pseudo-random float2 from float2 input
float2 hash22(float2 p) 
{
    // credit to Dave_Hoskins - https://www.shadertoy.com/view/4djSRW
    float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.xx + p3.yz) * p3.zy);
}

// Helper function: calculate squared length of vector
float lengthSqr(float2 v) 
{
    return dot(v, v);
}

float4 TextureBombing(Texture2D texture, SamplerState textureSampler, float2 uv, float tiling = 1.0, float scrambleFreq = 2.0)
{
    
    // Scale UV coordinates
    float2 scaledUV = -scrambleFreq * tiling * uv;
    
    float2 uvFloor = floor(scaledUV);
    float2 uvFrac = scaledUV - uvFloor;
    
    // Calculate splat centers for the two triangles in each quad
    float2 splatCenters[2];
    splatCenters[0] = uvFrac.x < 1.0 - uvFrac.y ? float2(0.0, 0.0) : float2(1.0, 1.0);
    splatCenters[1] = uvFrac.x < uvFrac.y      ? float2(0.0, 1.0) : float2(1.0, 0.0);
    splatCenters[0] += uvFloor;
    splatCenters[1] += uvFloor;
    
    // Calculate weights for blending
    float2 weights;
    weights[0] = max(1.0 - 2.0 * lengthSqr(scaledUV - splatCenters[0]), 0.0);
    weights[1] = max(1.0 - 2.0 * lengthSqr(scaledUV - splatCenters[1]), 0.0);
    
    // Normalize weights with epsilon to prevent division by zero/NaN
    float weightSum = dot(weights, float2(1.0, 1.0));
    weights /= max(weightSum, 1e-7); // Add small epsilon to avoid division by zero
    
    // Static tweak value (since we don't have time input)
    float tweak = 0.5; // Could be parameterized later
    
    // Explicit gradients for consistent mip selection
    float2 sampleUV = scaledUV / scrambleFreq;
    float2 duv_dx = ddx(sampleUV);
    float2 duv_dy = ddy(sampleUV);
    
    // Sample texture with random offsets for each splat center
    float3 col = float3(0.0, 0.0, 0.0);
    col += weights[0] * texture.SampleGrad(textureSampler, sampleUV + tweak * (hash22(splatCenters[0]) - 0.5), duv_dx, duv_dy).rgb;
    col += weights[1] * texture.SampleGrad(textureSampler, sampleUV + tweak * (hash22(splatCenters[1]) - 0.5), duv_dx, duv_dy).rgb;
    
    // Contrast correction
    float3 mean = texture.SampleBias(textureSampler, sampleUV, 16.0).rgb;
    col = mean + (col - mean) / length(weights);
    
    return float4(col, 1.0);
}

