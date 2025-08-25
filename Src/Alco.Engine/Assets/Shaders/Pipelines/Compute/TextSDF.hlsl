#include "Shaders/Libs/Core.hlsli"

struct Constants {
    float attenuation;      // Attenuation per pixel distance
};

// Font atlas textures (single channel but read as float4 to avoid compiler errors)
DEFINE_TEX2D_STORAGE(0, _frontBuffer, float4, "r8");
DEFINE_TEX2D_STORAGE(1, _backBuffer, float4, "r8");

PUSH_CONSTANT Constants constants;

/// <summary>
/// Applies linear attenuation to distance field value
/// </summary>
/// <param name="value">Current distance field value</param>
/// <param name="attenuation">Attenuation amount to subtract</param>
/// <returns>New distance field value with linear attenuation applied</returns>
float4 ApplyLinearAttenuation(float4 value, float attenuation) {
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
void MainCS(uint3 id: SV_DispatchThreadID) {
    uint2 pos = id.xy;
    
    // Read current distance field value
    float4 currentValue = _frontBuffer[pos];
    
    // Sample neighboring distance field values
    float4 neighbors[8] = {
        _frontBuffer[pos + int2(1, 0)],   // right
        _frontBuffer[pos + int2(-1, 0)],  // left  
        _frontBuffer[pos + int2(0, 1)],   // up
        _frontBuffer[pos + int2(0, -1)],  // down
        _frontBuffer[pos + int2(1, 1)],   // top-right
        _frontBuffer[pos + int2(-1, 1)],  // top-left
        _frontBuffer[pos + int2(1, -1)],  // bottom-right
        _frontBuffer[pos + int2(-1, -1)]  // bottom-left
    };
    
    float attenuation = constants.attenuation;
    float cornerAttenuation = attenuation * 1.414213562; // sqrt(2) for diagonal distance
    
    // Find maximum distance from neighbors with appropriate attenuation
    float4 maxValue = currentValue;
    
    // Check side neighbors (4-connected)
    maxValue = max(maxValue, ApplyLinearAttenuation(neighbors[0], attenuation));
    maxValue = max(maxValue, ApplyLinearAttenuation(neighbors[1], attenuation));
    maxValue = max(maxValue, ApplyLinearAttenuation(neighbors[2], attenuation));
    maxValue = max(maxValue, ApplyLinearAttenuation(neighbors[3], attenuation));
    
    // Check corner neighbors (8-connected)
    maxValue = max(maxValue, ApplyLinearAttenuation(neighbors[4], cornerAttenuation));
    maxValue = max(maxValue, ApplyLinearAttenuation(neighbors[5], cornerAttenuation));
    maxValue = max(maxValue, ApplyLinearAttenuation(neighbors[6], cornerAttenuation));
    maxValue = max(maxValue, ApplyLinearAttenuation(neighbors[7], cornerAttenuation));
    
    _backBuffer[pos] = maxValue;
}