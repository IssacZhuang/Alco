#include "Shaders/Libs/Core.hlsli"

DEFINE_TEX2D_SAMPLE(0, _texture);

// FXAA configuration constants
DEFINE_UNIFORM(1, _fxaaData) { 
    float2 InvFrameSize;      // 1.0 / FrameSize
    float Quality;            // Quality setting (0.5-2.0)
    float Threshold;          // Edge detection threshold (0.063-0.333)
};

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

// Calculate luminance using standard coefficients
float GetLuminance(float3 color) {
    return dot(color, float3(0.299, 0.587, 0.114));
}

// Sample texture with offset
float3 SampleOffset(float2 uv, float2 offset) {
    return SAMPLE_TEX2D(_texture, uv + offset * InvFrameSize).rgb;
}

// FXAA algorithm implementation
float3 FXAA(float2 uv) {
    float2 texelSize = InvFrameSize;
    
    // Sample center and neighboring pixels
    float3 rgbM = SAMPLE_TEX2D(_texture, uv).rgb;        // Center
    float3 rgbNW = SampleOffset(uv, float2(-1, -1));     // North-West
    float3 rgbNE = SampleOffset(uv, float2(1, -1));      // North-East
    float3 rgbSW = SampleOffset(uv, float2(-1, 1));      // South-West
    float3 rgbSE = SampleOffset(uv, float2(1, 1));       // South-East
    float3 rgbN = SampleOffset(uv, float2(0, -1));       // North
    float3 rgbS = SampleOffset(uv, float2(0, 1));        // South
    float3 rgbW = SampleOffset(uv, float2(-1, 0));       // West
    float3 rgbE = SampleOffset(uv, float2(1, 0));        // East
    
    // Convert to luminance
    float lumM = GetLuminance(rgbM);
    float lumNW = GetLuminance(rgbNW);
    float lumNE = GetLuminance(rgbNE);
    float lumSW = GetLuminance(rgbSW);
    float lumSE = GetLuminance(rgbSE);
    float lumN = GetLuminance(rgbN);
    float lumS = GetLuminance(rgbS);
    float lumW = GetLuminance(rgbW);
    float lumE = GetLuminance(rgbE);
    
    // Find min and max luminance in the local neighborhood
    float lumMin = min(lumM, min(min(lumN, lumS), min(lumW, lumE)));
    float lumMax = max(lumM, max(max(lumN, lumS), max(lumW, lumE)));
    float lumRange = lumMax - lumMin;
    
    // If the luminance range is too small, skip AA
    if (lumRange < max(Threshold, lumMax * 0.125)) {
        return rgbM;
    }
    
    // Determine edge direction
    float lumL = (lumN + lumS + lumW + lumE) * 0.25;
    float gradN = abs(lumN - lumL);
    float gradS = abs(lumS - lumL);
    float gradW = abs(lumW - lumL);
    float gradE = abs(lumE - lumL);
    
    float gradNS = gradN + gradS;
    float gradWE = gradW + gradE;
    
    bool isHorizontal = gradNS >= gradWE;
    
    // Choose samples along the edge
    float lum1 = isHorizontal ? lumS : lumW;
    float lum2 = isHorizontal ? lumN : lumE;
    float gradient1 = abs(lum1 - lumM);
    float gradient2 = abs(lum2 - lumM);
    
    bool is1Steepest = gradient1 >= gradient2;
    float gradientScaled = 0.25 * max(gradient1, gradient2);
    
    // Choose step size
    float stepLength = isHorizontal ? texelSize.y : texelSize.x;
    float avgLum = (lum1 + lum2) * 0.5;
    
    if (!is1Steepest) {
        stepLength = -stepLength;
    }
    
    // Initial offset
    float2 offset = isHorizontal ? float2(texelSize.x, 0) : float2(0, texelSize.y);
    float2 uv1 = uv;
    float2 uv2 = uv;
    
    if (isHorizontal) {
        uv1.y += stepLength * 0.5;
        uv2.y -= stepLength * 0.5;
    } else {
        uv1.x += stepLength * 0.5;
        uv2.x -= stepLength * 0.5;
    }
    
    // Walk along the edge to find the edge endpoints
    float lumaEnd1 = GetLuminance(SAMPLE_TEX2D(_texture, uv1).rgb);
    float lumaEnd2 = GetLuminance(SAMPLE_TEX2D(_texture, uv2).rgb);
    lumaEnd1 -= avgLum;
    lumaEnd2 -= avgLum;
    
    bool reached1 = abs(lumaEnd1) >= gradientScaled;
    bool reached2 = abs(lumaEnd2) >= gradientScaled;
    bool reachedBoth = reached1 && reached2;
    
    if (!reached1) {
        uv1 += offset;
    }
    if (!reached2) {
        uv2 -= offset;
    }
    
    // Continue searching if needed
    if (!reachedBoth) {
        for (int i = 2; i < 12; i++) {
            if (!reached1) {
                lumaEnd1 = GetLuminance(SAMPLE_TEX2D(_texture, uv1).rgb);
                lumaEnd1 -= avgLum;
            }
            if (!reached2) {
                lumaEnd2 = GetLuminance(SAMPLE_TEX2D(_texture, uv2).rgb);
                lumaEnd2 -= avgLum;
            }
            
            reached1 = abs(lumaEnd1) >= gradientScaled;
            reached2 = abs(lumaEnd2) >= gradientScaled;
            reachedBoth = reached1 && reached2;
            
            if (reachedBoth) break;
            
            if (!reached1) {
                uv1 += offset;
            }
            if (!reached2) {
                uv2 -= offset;
            }
        }
    }
    
    // Calculate distances to edge endpoints
    float distance1 = isHorizontal ? (uv.x - uv1.x) : (uv.y - uv1.y);
    float distance2 = isHorizontal ? (uv2.x - uv.x) : (uv2.y - uv.y);
    
    bool isDirection1 = distance1 < distance2;
    float distanceFinal = min(distance1, distance2);
    
    float edgeThickness = (distance1 + distance2);
    
    // Calculate sub-pixel offset
    float pixelOffset = (-2.0 * distanceFinal + edgeThickness) / edgeThickness;
    
    // Final sample position
    float2 finalUv = uv;
    if (isHorizontal) {
        finalUv.y += pixelOffset * stepLength;
    } else {
        finalUv.x += pixelOffset * stepLength;
    }
    
    return SAMPLE_TEX2D(_texture, finalUv).rgb;
}

[shader("pixel")]
float4 MainPS(V2F input) : SV_TARGET {
    float3 color = FXAA(input.uv);
    return float4(color, 1.0);
} 