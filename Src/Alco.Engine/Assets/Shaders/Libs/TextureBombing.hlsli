#include "Shaders/Libs/Core.hlsli"
#include "Shaders/Libs/Noise.hlsli"

// Simple hash function for generating pseudo-random values
float2 Hash2D(float2 p) {
    p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
    return -1.0 + 2.0 * frac(sin(p) * 43758.5453123);
}

// Generate a pseudo-random rotation matrix for 2D
float2x2 GetRandomRotation(float2 cellPos) {
    float angle = Hash2D(cellPos).x * PI * 2.0;
    float c = cos(angle);
    float s = sin(angle);
    return float2x2(c, -s, s, c);
}

// Main texture bombing function
// texture: The texture to sample
// textureSampler: The sampler state for the texture
// uv: Input UV coordinates  
// tiling: How many times the texture should tile
// offset: Additional offset for the UV coordinates
float4 TextureBombing(Texture2D texture, SamplerState textureSampler, float2 uv, float2 tiling, float2 offset) {
    // Apply tiling and offset
    float2 tiledUV = uv * tiling + offset;
    
    // Get the cell coordinates
    float2 cellPos = floor(tiledUV);
    float2 cellUV = frac(tiledUV);
    
    // Initialize output color
    float4 result = float4(0, 0, 0, 0);
    float totalWeight = 0.0;
    
    // Sample from current cell and 8 surrounding cells (3x3 grid)
    for (int x = -1; x <= 1; x++) {
        for (int y = -1; y <= 1; y++) {
            float2 neighborCell = cellPos + float2(x, y);
            
            // Generate random offset for this cell
            float2 randomOffset = Hash2D(neighborCell) * 0.5; // Limit offset to half cell size
            
            // Generate random rotation for this cell
            float2x2 randomRotation = GetRandomRotation(neighborCell);
            
            // Transform UV coordinates
            float2 rotatedUV = mul(randomRotation, cellUV - 0.5) + 0.5;
            float2 sampleUV = (neighborCell + rotatedUV + randomOffset) / tiling;
            
            // Sample the texture
            float4 sampleColor = texture.Sample(textureSampler, sampleUV);
            
            // Calculate weight based on distance from cell center
            float2 distFromCenter = abs(cellUV - 0.5 - float2(x, y));
            float weight = 1.0 - max(distFromCenter.x, distFromCenter.y) * 2.0;
            weight = saturate(weight);
            
            // Apply smooth falloff
            weight = smoothstep(0.0, 1.0, weight);
            
            // Add random variation to weight
            float randomFactor = Hash2D(neighborCell + float2(0.5, 0.5)).x * 0.5 + 0.5;
            weight *= randomFactor;
            
            result += sampleColor * weight;
            totalWeight += weight;
        }
    }
    
    // Normalize by total weight to avoid darkening
    if (totalWeight > 0.0) {
        result /= totalWeight;
    }
    
    return result;
}

// Simplified version with fewer parameters
float4 TextureBombing(Texture2D texture, SamplerState textureSampler, float2 uv) {
    return TextureBombing(texture, textureSampler, uv, float2(1, 1), float2(0, 0));
}

// Version with intensity control
float4 TextureBombing(Texture2D texture, SamplerState textureSampler, float2 uv, float2 tiling, float2 offset, float intensity) {
    float4 bombedTexture = TextureBombing(texture, textureSampler, uv, tiling, offset);
    float4 regularTexture = texture.Sample(textureSampler, uv * tiling + offset);
    
    return lerp(regularTexture, bombedTexture, intensity);
}