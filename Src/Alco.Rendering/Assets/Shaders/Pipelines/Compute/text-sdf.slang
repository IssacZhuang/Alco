// file "text-sdf.slang" - module "text_s_d_f"
//
// Slang module converted from Shaders/Pipelines/Compute/TextSDF.hlsl (Phase 2 migration).
// Resources are grouped as ParameterBlocks — the compiler assigns each block
// its own descriptor set in declaration order; members resolve by bare field name.

#language slang 2025
module text_sdf;

import alco_rendering_core;

struct Constants {
    float attenuation;      // Attenuation per pixel distance
};

// Font atlas textures (single channel but read as float4 to avoid compiler errors)
public struct FrontBufferParams
{
    public [[vk::image_format("r8")]] RWTexture2D<float4> frontBuffer;
};

public ParameterBlock<FrontBufferParams> frontBuffer;

public struct BackBufferParams
{
    public [[vk::image_format("r8")]] RWTexture2D<float4> backBuffer;
};

public ParameterBlock<BackBufferParams> backBuffer;

[[vk::push_constant]] Constants constants;

/// <summary>
/// Applies linear attenuation to distance field value
/// </summary>
/// <param name="value">Current distance field value</param>
/// <param name="attenuation">Attenuation amount to subtract</param>
/// <returns>New distance field value with linear attenuation applied</returns>
float4 applyLinearAttenuation(float4 value, float attenuation) {
    // For SDF, we work with the red channel only
    float distance = value.r;
    
    if (distance < 1e-6) {
        return float4(0, 0, 0, value.a);
    }
    
    // Linear distance decay: newDistance = max(0, originalDistance - attenuation)
    float newDistance = max(0.0, distance - attenuation);
    return float4(newDistance, newDistance, newDistance, value.a);
}

[shader("compute")]
[numthreads(16, 16, 1)]
void mainCS(uint3 id: SV_DispatchThreadID) {
    uint2 pos = id.xy;
    
    // Read current distance field value
    float4 currentValue = frontBuffer.frontBuffer[pos];
    
    // Sample neighboring distance field values
    float4 neighbors[8] = {
        frontBuffer.frontBuffer[pos + int2(1, 0)],   // right
        frontBuffer.frontBuffer[pos + int2(-1, 0)],  // left  
        frontBuffer.frontBuffer[pos + int2(0, 1)],   // up
        frontBuffer.frontBuffer[pos + int2(0, -1)],  // down
        frontBuffer.frontBuffer[pos + int2(1, 1)],   // top-right
        frontBuffer.frontBuffer[pos + int2(-1, 1)],  // top-left
        frontBuffer.frontBuffer[pos + int2(1, -1)],  // bottom-right
        frontBuffer.frontBuffer[pos + int2(-1, -1)]  // bottom-left
    };
    
    float attenuation = constants.attenuation;
    float cornerAttenuation = attenuation * 1.414213562; // sqrt(2) for diagonal distance
    
    // Find maximum distance from neighbors with appropriate attenuation
    float4 maxValue = currentValue;
    
    // Check side neighbors (4-connected)
    maxValue = max(maxValue, applyLinearAttenuation(neighbors[0], attenuation));
    maxValue = max(maxValue, applyLinearAttenuation(neighbors[1], attenuation));
    maxValue = max(maxValue, applyLinearAttenuation(neighbors[2], attenuation));
    maxValue = max(maxValue, applyLinearAttenuation(neighbors[3], attenuation));
    
    // Check corner neighbors (8-connected)
    maxValue = max(maxValue, applyLinearAttenuation(neighbors[4], cornerAttenuation));
    maxValue = max(maxValue, applyLinearAttenuation(neighbors[5], cornerAttenuation));
    maxValue = max(maxValue, applyLinearAttenuation(neighbors[6], cornerAttenuation));
    maxValue = max(maxValue, applyLinearAttenuation(neighbors[7], cornerAttenuation));
    
    backBuffer.backBuffer[pos] = maxValue;
}