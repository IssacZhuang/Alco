#include "Shaders/Libs/Core.hlsli"

DEFINE_TEX2D_SAMPLE(0, _texture);

// FXAA configuration constants
DEFINE_UNIFORM(1, _fxaaData) {
  float2 InvFrameSize; // 1.0 / FrameSize
  float Threshold;     // Edge detection threshold (0.063-0.333)
  float _padding;      // Padding for alignment
};

struct Vertex {
  float3 position : POSITION;
  float2 uv : TEXCOORD0;
};

struct V2F {
  float4 position : SV_POSITION;
  float2 uv : TEXCOORD0;
};

[shader("vertex")] V2F MainVS(Vertex input) {
  V2F output = (V2F)0;
  output.position = float4(input.position, 1.0f);
  output.uv = input.uv;
  return output;
}

/*============================================================================
                        FXAA 3.11 QUALITY PRESETS
============================================================================*/

// Define quality presets - only one should be defined via shader defines
// FXAA_QUALITY_LOW    - 4 steps,  fastest performance
// FXAA_QUALITY_MEDIUM - 8 steps,  balanced
// FXAA_QUALITY_HIGH   - 12 steps, high quality (default)
// FXAA_QUALITY_ULTRA  - 29 steps, maximum quality

#if defined(FXAA_QUALITY_LOW)
#define NUM_SAMPLES 4
static const float s_SampleDistances[NUM_SAMPLES] = {
  1.5,
  3.0,
  12.0,
  12.0
};
#elif defined(FXAA_QUALITY_MEDIUM)
#define NUM_SAMPLES 8
static const float s_SampleDistances[NUM_SAMPLES] = {
  1.0,
  1.5,
  2.0,
  2.0,
  2.0,
  2.0,
  4.0,
  8.0
};
#elif defined(FXAA_QUALITY_HIGH)
#define NUM_SAMPLES 12
static const float s_SampleDistances[NUM_SAMPLES] = {
  1.0,
  1.5,
  2.0,
  2.0,
  2.0,
  2.0,
  2.0,
  2.0,
  2.0,
  4.0,
  8.0,
  8.0
};
#elif defined(FXAA_QUALITY_ULTRA)
#define NUM_SAMPLES 29
static const float s_SampleDistances[NUM_SAMPLES] = {
  1.0,
  1.0,
  1.0,
  1.0,
  1.0,
  1.5,
  2.0,
  2.0,
  2.0,
  2.0,
  4.0,
  8.0,
  8.0,
  8.0,
  8.0,
  8.0,
  8.0,
  8.0,
  8.0,
  8.0,
  8.0,
  8.0,
  8.0,
  8.0,
  8.0,
  8.0,
  8.0,
  8.0,
  8.0
};
#else
// Default to HIGH quality if no preset is defined
#define NUM_SAMPLES 12
static const float s_SampleDistances[NUM_SAMPLES] = {
  1.0,
  1.5,
  2.0,
  2.0,
  2.0,
  2.0,
  2.0,
  2.0,
  2.0,
  4.0,
  8.0,
  8.0
};
#endif

/*============================================================================
                        FXAA 3.11 ALGORITHM
============================================================================*/

// Convert RGB to luma (perceptual luminance)
float FxaaLuma(float3 rgb) {
  return rgb.y * (0.587 / 0.299) + rgb.x;
}

float4 FxaaPixelShader(float2 pos,     // Pixel position
                       float2 rcpFrame // 1.0 / screen size
) {
  // FXAA algorithm parameters
  float fxaaQualitySubpix = 0.75; // Subpixel aliasing removal amount (standard value)
  float fxaaQualityEdgeThreshold = Threshold; // Edge detection threshold
  float fxaaQualityEdgeThresholdMin = 0.0625; // Minimum threshold

  /*--------------------------------------------------------------------------*/
  // LOCAL CONTRAST CHECK
  /*--------------------------------------------------------------------------*/

  // Sample center and surrounding pixels
  float3 rgbN = SAMPLE_TEX2D(_texture, pos + float2(0.0, -1.0) * rcpFrame).rgb;
  float3 rgbW = SAMPLE_TEX2D(_texture, pos + float2(-1.0, 0.0) * rcpFrame).rgb;
  float3 rgbM = SAMPLE_TEX2D(_texture, pos).rgb;
  float3 rgbE = SAMPLE_TEX2D(_texture, pos + float2(1.0, 0.0) * rcpFrame).rgb;
  float3 rgbS = SAMPLE_TEX2D(_texture, pos + float2(0.0, 1.0) * rcpFrame).rgb;

  // Convert to luma
  float lumaN = FxaaLuma(rgbN);
  float lumaW = FxaaLuma(rgbW);
  float lumaM = FxaaLuma(rgbM);
  float lumaE = FxaaLuma(rgbE);
  float lumaS = FxaaLuma(rgbS);

  // Find the maximum and minimum luma around this pixel
  float rangeMin = min(lumaM, min(min(lumaN, lumaW), min(lumaS, lumaE)));
  float rangeMax = max(lumaM, max(max(lumaN, lumaW), max(lumaS, lumaE)));
  float range = rangeMax - rangeMin;

  // Early exit if the variation in local luma is lower than a threshold
  if (range <
      max(fxaaQualityEdgeThresholdMin, rangeMax * fxaaQualityEdgeThreshold)) {
    return float4(rgbM, 1.0);
  }

  /*--------------------------------------------------------------------------*/
  // SUBPIXEL ALIASING TEST
  /*--------------------------------------------------------------------------*/

  // Sample the corners
  float3 rgbL = rgbN + rgbW + rgbM + rgbE + rgbS;

  float3 rgbNW =
      SAMPLE_TEX2D(_texture, pos + float2(-1.0, -1.0) * rcpFrame).rgb;
  float3 rgbNE = SAMPLE_TEX2D(_texture, pos + float2(1.0, -1.0) * rcpFrame).rgb;
  float3 rgbSW = SAMPLE_TEX2D(_texture, pos + float2(-1.0, 1.0) * rcpFrame).rgb;
  float3 rgbSE = SAMPLE_TEX2D(_texture, pos + float2(1.0, 1.0) * rcpFrame).rgb;

  rgbL += (rgbNW + rgbNE + rgbSW + rgbSE);
  rgbL *= (1.0 / 9.0);

  float lumaNW = FxaaLuma(rgbNW);
  float lumaNE = FxaaLuma(rgbNE);
  float lumaSW = FxaaLuma(rgbSW);
  float lumaSE = FxaaLuma(rgbSE);

  // Compute the subpixel offset
  float lumaL = (lumaN + lumaW + lumaE + lumaS) * 0.25;
  float rangeL = abs(lumaL - lumaM);
  float blendL = max(0.0, (rangeL / range) - 0.25) * 1.333;
  blendL = min(fxaaQualitySubpix, blendL);

  /*--------------------------------------------------------------------------*/
  // EDGE ORIENTATION
  /*--------------------------------------------------------------------------*/

  // Compute vertical and horizontal edge strength
  float edgeVert = abs((0.25 * lumaNW) + (-0.5 * lumaN) + (0.25 * lumaNE)) +
                   abs((0.50 * lumaW) + (-1.0 * lumaM) + (0.50 * lumaE)) +
                   abs((0.25 * lumaSW) + (-0.5 * lumaS) + (0.25 * lumaSE));

  float edgeHorz = abs((0.25 * lumaNW) + (-0.5 * lumaW) + (0.25 * lumaSW)) +
                   abs((0.50 * lumaN) + (-1.0 * lumaM) + (0.50 * lumaS)) +
                   abs((0.25 * lumaNE) + (-0.5 * lumaE) + (0.25 * lumaSE));

  bool horzSpan = edgeHorz >= edgeVert;

  /*--------------------------------------------------------------------------*/
  // EDGE DIRECTION
  /*--------------------------------------------------------------------------*/

  // Choose the step direction based on edge orientation
  float lengthSign = horzSpan ? -rcpFrame.y : -rcpFrame.x;

  if (!horzSpan) {
    lumaN = lumaW;
    lumaS = lumaE;
  }

  float gradientN = abs(lumaN - lumaM);
  float gradientS = abs(lumaS - lumaM);
  lumaN = (lumaN + lumaM) * 0.5;
  lumaS = (lumaS + lumaM) * 0.5;

  // Choose the side with the largest gradient
  bool pairN = gradientN >= gradientS;
  if (!pairN) {
    lumaN = lumaS;
    gradientN = gradientS;
    lengthSign *= -1.0;
  }

  /*--------------------------------------------------------------------------*/
  // SEARCH ALONG EDGE (FXAA 3.11 Variable Step Search)
  /*--------------------------------------------------------------------------*/

  float2 posB = pos;
  float2 offNP = horzSpan ? float2(rcpFrame.x, 0.0) : float2(0.0, rcpFrame.y);

  posB.x += (!horzSpan) ? 0.0 : lengthSign * 0.5;
  posB.y += horzSpan ? 0.0 : lengthSign * 0.5;

  float2 posN = posB;
  float2 posP = posB;

  float lumaEndN = lumaN;
  float lumaEndP = lumaN;
  bool doneN = false;
  bool doneP = false;

  // Variable step search using the sample distances array
  [unroll]
  for (int i = 0; i < NUM_SAMPLES; i++) {
    if (!doneN) {
      posN -= offNP * s_SampleDistances[i];
      lumaEndN = FxaaLuma(SAMPLE_TEX2D(_texture, posN).rgb);
      doneN = abs(lumaEndN - lumaN) >= gradientN;
    }
    
    if (!doneP) {
      posP += offNP * s_SampleDistances[i];
      lumaEndP = FxaaLuma(SAMPLE_TEX2D(_texture, posP).rgb);
      doneP = abs(lumaEndP - lumaN) >= gradientN;
    }
    
    // Early exit if both directions are done
    if (doneN && doneP) {
      break;
    }
  }

/*--------------------------------------------------------------------------*/
// HANDLE EDGE ENDINGS
/*--------------------------------------------------------------------------*/

float dstN = horzSpan ? pos.x - posN.x : pos.y - posN.y;
float dstP = horzSpan ? posP.x - pos.x : posP.y - pos.y;
bool directionN = dstN < dstP;
lumaEndN = directionN ? lumaEndN : lumaEndP;

// Check if end point luma is on the opposite side
if (((lumaEndN - lumaN) < 0.0) == ((lumaM - lumaN) < 0.0)) {
  lengthSign = 0.0;
}

/*--------------------------------------------------------------------------*/
// FINAL BLEND
/*--------------------------------------------------------------------------*/

float spanLength = (dstP + dstN);
dstN = directionN ? dstN : dstP;
float subPixelOffset = (0.5 + (dstN * (-1.0 / spanLength))) * lengthSign;

// Apply subpixel shift
float3 rgbF =
    SAMPLE_TEX2D(_texture, float2(pos.x + (horzSpan ? 0.0 : subPixelOffset),
                                  pos.y + (horzSpan ? subPixelOffset : 0.0)))
        .rgb;

// Blend with subpixel anti-aliasing
return float4(lerp(rgbF, rgbL, blendL), 1.0);
}

[shader("pixel")] float4 MainPS(V2F input) : SV_TARGET {
  return FxaaPixelShader(input.uv, InvFrameSize);
}