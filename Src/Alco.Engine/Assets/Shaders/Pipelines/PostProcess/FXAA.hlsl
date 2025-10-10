/*============================================================================
                    NVIDIA FXAA 3.11

    Fast Approximate Anti-Aliasing (FXAA) v3.11
    Copyright (c) 2010-2011, NVIDIA Corporation

    Implementation based on NVIDIA's FXAA 3.11 white paper
    Adapted for HLSL by the Alco Engine team
============================================================================*/

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
#define FXAA_QUALITY_PS 4
#define FXAA_QUALITY_P0 1.5
#define FXAA_QUALITY_P1 3.0
#define FXAA_QUALITY_P2 12.0
#define FXAA_QUALITY_P3 12.0
#elif defined(FXAA_QUALITY_MEDIUM)
#define FXAA_QUALITY_PS 8
#define FXAA_QUALITY_P0 1.0
#define FXAA_QUALITY_P1 1.5
#define FXAA_QUALITY_P2 2.0
#define FXAA_QUALITY_P3 2.0
#define FXAA_QUALITY_P4 2.0
#define FXAA_QUALITY_P5 2.0
#define FXAA_QUALITY_P6 4.0
#define FXAA_QUALITY_P7 8.0
#elif defined(FXAA_QUALITY_HIGH)
#define FXAA_QUALITY_PS 12
#define FXAA_QUALITY_P0 1.0
#define FXAA_QUALITY_P1 1.5
#define FXAA_QUALITY_P2 2.0
#define FXAA_QUALITY_P3 2.0
#define FXAA_QUALITY_P4 2.0
#define FXAA_QUALITY_P5 2.0
#define FXAA_QUALITY_P6 2.0
#define FXAA_QUALITY_P7 2.0
#define FXAA_QUALITY_P8 2.0
#define FXAA_QUALITY_P9 4.0
#define FXAA_QUALITY_P10 8.0
#define FXAA_QUALITY_P11 8.0
#elif defined(FXAA_QUALITY_ULTRA)
#define FXAA_QUALITY_PS 29
#define FXAA_QUALITY_P0 1.0
#define FXAA_QUALITY_P1 1.0
#define FXAA_QUALITY_P2 1.0
#define FXAA_QUALITY_P3 1.0
#define FXAA_QUALITY_P4 1.0
#define FXAA_QUALITY_P5 1.5
#define FXAA_QUALITY_P6 2.0
#define FXAA_QUALITY_P7 2.0
#define FXAA_QUALITY_P8 2.0
#define FXAA_QUALITY_P9 2.0
#define FXAA_QUALITY_P10 4.0
#define FXAA_QUALITY_P11 8.0
#define FXAA_QUALITY_P12 8.0
#define FXAA_QUALITY_P13 8.0
#define FXAA_QUALITY_P14 8.0
#define FXAA_QUALITY_P15 8.0
#define FXAA_QUALITY_P16 8.0
#define FXAA_QUALITY_P17 8.0
#define FXAA_QUALITY_P18 8.0
#define FXAA_QUALITY_P19 8.0
#define FXAA_QUALITY_P20 8.0
#define FXAA_QUALITY_P21 8.0
#define FXAA_QUALITY_P22 8.0
#define FXAA_QUALITY_P23 8.0
#define FXAA_QUALITY_P24 8.0
#define FXAA_QUALITY_P25 8.0
#define FXAA_QUALITY_P26 8.0
#define FXAA_QUALITY_P27 8.0
#define FXAA_QUALITY_P28 8.0
#else
// Default to HIGH quality if no preset is defined
#define FXAA_QUALITY_PS 12
#define FXAA_QUALITY_P0 1.0
#define FXAA_QUALITY_P1 1.5
#define FXAA_QUALITY_P2 2.0
#define FXAA_QUALITY_P3 2.0
#define FXAA_QUALITY_P4 2.0
#define FXAA_QUALITY_P5 2.0
#define FXAA_QUALITY_P6 2.0
#define FXAA_QUALITY_P7 2.0
#define FXAA_QUALITY_P8 2.0
#define FXAA_QUALITY_P9 4.0
#define FXAA_QUALITY_P10 8.0
#define FXAA_QUALITY_P11 8.0
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
  float fxaaQualitySubpix =
      0.75; // Subpixel aliasing removal amount (standard value)
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

// Variable step search - each iteration uses progressively larger steps
// This is the key optimization of FXAA 3.11
#if (FXAA_QUALITY_PS > 0)
  if (!doneN)
    posN -= offNP * FXAA_QUALITY_P0;
  if (!doneP)
    posP += offNP * FXAA_QUALITY_P0;
  if (!doneN)
    lumaEndN = FxaaLuma(SAMPLE_TEX2D(_texture, posN).rgb);
  if (!doneP)
    lumaEndP = FxaaLuma(SAMPLE_TEX2D(_texture, posP).rgb);
  if (!doneN)
    doneN = abs(lumaEndN - lumaN) >= gradientN;
  if (!doneP)
    doneP = abs(lumaEndP - lumaN) >= gradientN;
  if (!doneN && !doneP) {
#endif

#if (FXAA_QUALITY_PS > 1)
    if (!doneN)
      posN -= offNP * FXAA_QUALITY_P1;
    if (!doneP)
      posP += offNP * FXAA_QUALITY_P1;
    if (!doneN)
      lumaEndN = FxaaLuma(SAMPLE_TEX2D(_texture, posN).rgb);
    if (!doneP)
      lumaEndP = FxaaLuma(SAMPLE_TEX2D(_texture, posP).rgb);
    if (!doneN)
      doneN = abs(lumaEndN - lumaN) >= gradientN;
    if (!doneP)
      doneP = abs(lumaEndP - lumaN) >= gradientN;
    if (!doneN && !doneP) {
#endif

#if (FXAA_QUALITY_PS > 2)
      if (!doneN)
        posN -= offNP * FXAA_QUALITY_P2;
      if (!doneP)
        posP += offNP * FXAA_QUALITY_P2;
      if (!doneN)
        lumaEndN = FxaaLuma(SAMPLE_TEX2D(_texture, posN).rgb);
      if (!doneP)
        lumaEndP = FxaaLuma(SAMPLE_TEX2D(_texture, posP).rgb);
      if (!doneN)
        doneN = abs(lumaEndN - lumaN) >= gradientN;
      if (!doneP)
        doneP = abs(lumaEndP - lumaN) >= gradientN;
      if (!doneN && !doneP) {
#endif

#if (FXAA_QUALITY_PS > 3)
        if (!doneN)
          posN -= offNP * FXAA_QUALITY_P3;
        if (!doneP)
          posP += offNP * FXAA_QUALITY_P3;
        if (!doneN)
          lumaEndN = FxaaLuma(SAMPLE_TEX2D(_texture, posN).rgb);
        if (!doneP)
          lumaEndP = FxaaLuma(SAMPLE_TEX2D(_texture, posP).rgb);
        if (!doneN)
          doneN = abs(lumaEndN - lumaN) >= gradientN;
        if (!doneP)
          doneP = abs(lumaEndP - lumaN) >= gradientN;
        if (!doneN && !doneP) {
#endif

#if (FXAA_QUALITY_PS > 4)
          if (!doneN)
            posN -= offNP * FXAA_QUALITY_P4;
          if (!doneP)
            posP += offNP * FXAA_QUALITY_P4;
          if (!doneN)
            lumaEndN = FxaaLuma(SAMPLE_TEX2D(_texture, posN).rgb);
          if (!doneP)
            lumaEndP = FxaaLuma(SAMPLE_TEX2D(_texture, posP).rgb);
          if (!doneN)
            doneN = abs(lumaEndN - lumaN) >= gradientN;
          if (!doneP)
            doneP = abs(lumaEndP - lumaN) >= gradientN;
          if (!doneN && !doneP) {
#endif

#if (FXAA_QUALITY_PS > 5)
            if (!doneN)
              posN -= offNP * FXAA_QUALITY_P5;
            if (!doneP)
              posP += offNP * FXAA_QUALITY_P5;
            if (!doneN)
              lumaEndN = FxaaLuma(SAMPLE_TEX2D(_texture, posN).rgb);
            if (!doneP)
              lumaEndP = FxaaLuma(SAMPLE_TEX2D(_texture, posP).rgb);
            if (!doneN)
              doneN = abs(lumaEndN - lumaN) >= gradientN;
            if (!doneP)
              doneP = abs(lumaEndP - lumaN) >= gradientN;
            if (!doneN && !doneP) {
#endif

#if (FXAA_QUALITY_PS > 6)
              if (!doneN)
                posN -= offNP * FXAA_QUALITY_P6;
              if (!doneP)
                posP += offNP * FXAA_QUALITY_P6;
              if (!doneN)
                lumaEndN = FxaaLuma(SAMPLE_TEX2D(_texture, posN).rgb);
              if (!doneP)
                lumaEndP = FxaaLuma(SAMPLE_TEX2D(_texture, posP).rgb);
              if (!doneN)
                doneN = abs(lumaEndN - lumaN) >= gradientN;
              if (!doneP)
                doneP = abs(lumaEndP - lumaN) >= gradientN;
              if (!doneN && !doneP) {
#endif

#if (FXAA_QUALITY_PS > 7)
                if (!doneN)
                  posN -= offNP * FXAA_QUALITY_P7;
                if (!doneP)
                  posP += offNP * FXAA_QUALITY_P7;
                if (!doneN)
                  lumaEndN = FxaaLuma(SAMPLE_TEX2D(_texture, posN).rgb);
                if (!doneP)
                  lumaEndP = FxaaLuma(SAMPLE_TEX2D(_texture, posP).rgb);
                if (!doneN)
                  doneN = abs(lumaEndN - lumaN) >= gradientN;
                if (!doneP)
                  doneP = abs(lumaEndP - lumaN) >= gradientN;
                if (!doneN && !doneP) {
#endif

#if (FXAA_QUALITY_PS > 8)
                  if (!doneN)
                    posN -= offNP * FXAA_QUALITY_P8;
                  if (!doneP)
                    posP += offNP * FXAA_QUALITY_P8;
                  if (!doneN)
                    lumaEndN = FxaaLuma(SAMPLE_TEX2D(_texture, posN).rgb);
                  if (!doneP)
                    lumaEndP = FxaaLuma(SAMPLE_TEX2D(_texture, posP).rgb);
                  if (!doneN)
                    doneN = abs(lumaEndN - lumaN) >= gradientN;
                  if (!doneP)
                    doneP = abs(lumaEndP - lumaN) >= gradientN;
                  if (!doneN && !doneP) {
#endif

#if (FXAA_QUALITY_PS > 9)
                    if (!doneN)
                      posN -= offNP * FXAA_QUALITY_P9;
                    if (!doneP)
                      posP += offNP * FXAA_QUALITY_P9;
                    if (!doneN)
                      lumaEndN = FxaaLuma(SAMPLE_TEX2D(_texture, posN).rgb);
                    if (!doneP)
                      lumaEndP = FxaaLuma(SAMPLE_TEX2D(_texture, posP).rgb);
                    if (!doneN)
                      doneN = abs(lumaEndN - lumaN) >= gradientN;
                    if (!doneP)
                      doneP = abs(lumaEndP - lumaN) >= gradientN;
                    if (!doneN && !doneP) {
#endif

#if (FXAA_QUALITY_PS > 10)
                      if (!doneN)
                        posN -= offNP * FXAA_QUALITY_P10;
                      if (!doneP)
                        posP += offNP * FXAA_QUALITY_P10;
                      if (!doneN)
                        lumaEndN = FxaaLuma(SAMPLE_TEX2D(_texture, posN).rgb);
                      if (!doneP)
                        lumaEndP = FxaaLuma(SAMPLE_TEX2D(_texture, posP).rgb);
                      if (!doneN)
                        doneN = abs(lumaEndN - lumaN) >= gradientN;
                      if (!doneP)
                        doneP = abs(lumaEndP - lumaN) >= gradientN;
                      if (!doneN && !doneP) {
#endif

#if (FXAA_QUALITY_PS > 11)
                        if (!doneN)
                          posN -= offNP * FXAA_QUALITY_P11;
                        if (!doneP)
                          posP += offNP * FXAA_QUALITY_P11;
                        if (!doneN)
                          lumaEndN = FxaaLuma(SAMPLE_TEX2D(_texture, posN).rgb);
                        if (!doneP)
                          lumaEndP = FxaaLuma(SAMPLE_TEX2D(_texture, posP).rgb);
                        if (!doneN)
                          doneN = abs(lumaEndN - lumaN) >= gradientN;
                        if (!doneP)
                          doneP = abs(lumaEndP - lumaN) >= gradientN;
                        if (!doneN && !doneP) {
#endif

#if (FXAA_QUALITY_PS > 12)
                          if (!doneN)
                            posN -= offNP * FXAA_QUALITY_P12;
                          if (!doneP)
                            posP += offNP * FXAA_QUALITY_P12;
                          if (!doneN)
                            lumaEndN =
                                FxaaLuma(SAMPLE_TEX2D(_texture, posN).rgb);
                          if (!doneP)
                            lumaEndP =
                                FxaaLuma(SAMPLE_TEX2D(_texture, posP).rgb);
                          if (!doneN)
                            doneN = abs(lumaEndN - lumaN) >= gradientN;
                          if (!doneP)
                            doneP = abs(lumaEndP - lumaN) >= gradientN;
                          if (!doneN && !doneP) {
#endif

#if (FXAA_QUALITY_PS > 13)
                            if (!doneN)
                              posN -= offNP * FXAA_QUALITY_P13;
                            if (!doneP)
                              posP += offNP * FXAA_QUALITY_P13;
                            if (!doneN)
                              lumaEndN =
                                  FxaaLuma(SAMPLE_TEX2D(_texture, posN).rgb);
                            if (!doneP)
                              lumaEndP =
                                  FxaaLuma(SAMPLE_TEX2D(_texture, posP).rgb);
                            if (!doneN)
                              doneN = abs(lumaEndN - lumaN) >= gradientN;
                            if (!doneP)
                              doneP = abs(lumaEndP - lumaN) >= gradientN;
                            if (!doneN && !doneP) {
#endif

#if (FXAA_QUALITY_PS > 14)
                              if (!doneN)
                                posN -= offNP * FXAA_QUALITY_P14;
                              if (!doneP)
                                posP += offNP * FXAA_QUALITY_P14;
                              if (!doneN)
                                lumaEndN =
                                    FxaaLuma(SAMPLE_TEX2D(_texture, posN).rgb);
                              if (!doneP)
                                lumaEndP =
                                    FxaaLuma(SAMPLE_TEX2D(_texture, posP).rgb);
                              if (!doneN)
                                doneN = abs(lumaEndN - lumaN) >= gradientN;
                              if (!doneP)
                                doneP = abs(lumaEndP - lumaN) >= gradientN;
                              if (!doneN && !doneP) {
#endif

#if (FXAA_QUALITY_PS > 15)
                                if (!doneN)
                                  posN -= offNP * FXAA_QUALITY_P15;
                                if (!doneP)
                                  posP += offNP * FXAA_QUALITY_P15;
                                if (!doneN)
                                  lumaEndN = FxaaLuma(
                                      SAMPLE_TEX2D(_texture, posN).rgb);
                                if (!doneP)
                                  lumaEndP = FxaaLuma(
                                      SAMPLE_TEX2D(_texture, posP).rgb);
                                if (!doneN)
                                  doneN = abs(lumaEndN - lumaN) >= gradientN;
                                if (!doneP)
                                  doneP = abs(lumaEndP - lumaN) >= gradientN;
                                if (!doneN && !doneP) {
#endif

#if (FXAA_QUALITY_PS > 16)
                                  if (!doneN)
                                    posN -= offNP * FXAA_QUALITY_P16;
                                  if (!doneP)
                                    posP += offNP * FXAA_QUALITY_P16;
                                  if (!doneN)
                                    lumaEndN = FxaaLuma(
                                        SAMPLE_TEX2D(_texture, posN).rgb);
                                  if (!doneP)
                                    lumaEndP = FxaaLuma(
                                        SAMPLE_TEX2D(_texture, posP).rgb);
                                  if (!doneN)
                                    doneN = abs(lumaEndN - lumaN) >= gradientN;
                                  if (!doneP)
                                    doneP = abs(lumaEndP - lumaN) >= gradientN;
                                  if (!doneN && !doneP) {
#endif

#if (FXAA_QUALITY_PS > 17)
                                    if (!doneN)
                                      posN -= offNP * FXAA_QUALITY_P17;
                                    if (!doneP)
                                      posP += offNP * FXAA_QUALITY_P17;
                                    if (!doneN)
                                      lumaEndN = FxaaLuma(
                                          SAMPLE_TEX2D(_texture, posN).rgb);
                                    if (!doneP)
                                      lumaEndP = FxaaLuma(
                                          SAMPLE_TEX2D(_texture, posP).rgb);
                                    if (!doneN)
                                      doneN =
                                          abs(lumaEndN - lumaN) >= gradientN;
                                    if (!doneP)
                                      doneP =
                                          abs(lumaEndP - lumaN) >= gradientN;
                                    if (!doneN && !doneP) {
#endif

#if (FXAA_QUALITY_PS > 18)
                                      if (!doneN)
                                        posN -= offNP * FXAA_QUALITY_P18;
                                      if (!doneP)
                                        posP += offNP * FXAA_QUALITY_P18;
                                      if (!doneN)
                                        lumaEndN = FxaaLuma(
                                            SAMPLE_TEX2D(_texture, posN).rgb);
                                      if (!doneP)
                                        lumaEndP = FxaaLuma(
                                            SAMPLE_TEX2D(_texture, posP).rgb);
                                      if (!doneN)
                                        doneN =
                                            abs(lumaEndN - lumaN) >= gradientN;
                                      if (!doneP)
                                        doneP =
                                            abs(lumaEndP - lumaN) >= gradientN;
                                      if (!doneN && !doneP) {
#endif

#if (FXAA_QUALITY_PS > 19)
                                        if (!doneN)
                                          posN -= offNP * FXAA_QUALITY_P19;
                                        if (!doneP)
                                          posP += offNP * FXAA_QUALITY_P19;
                                        if (!doneN)
                                          lumaEndN = FxaaLuma(
                                              SAMPLE_TEX2D(_texture, posN).rgb);
                                        if (!doneP)
                                          lumaEndP = FxaaLuma(
                                              SAMPLE_TEX2D(_texture, posP).rgb);
                                        if (!doneN)
                                          doneN = abs(lumaEndN - lumaN) >=
                                                  gradientN;
                                        if (!doneP)
                                          doneP = abs(lumaEndP - lumaN) >=
                                                  gradientN;
                                        if (!doneN && !doneP) {
#endif

#if (FXAA_QUALITY_PS > 20)
                                          if (!doneN)
                                            posN -= offNP * FXAA_QUALITY_P20;
                                          if (!doneP)
                                            posP += offNP * FXAA_QUALITY_P20;
                                          if (!doneN)
                                            lumaEndN = FxaaLuma(
                                                SAMPLE_TEX2D(_texture, posN)
                                                    .rgb);
                                          if (!doneP)
                                            lumaEndP = FxaaLuma(
                                                SAMPLE_TEX2D(_texture, posP)
                                                    .rgb);
                                          if (!doneN)
                                            doneN = abs(lumaEndN - lumaN) >=
                                                    gradientN;
                                          if (!doneP)
                                            doneP = abs(lumaEndP - lumaN) >=
                                                    gradientN;
                                          if (!doneN && !doneP) {
#endif

#if (FXAA_QUALITY_PS > 21)
                                            if (!doneN)
                                              posN -= offNP * FXAA_QUALITY_P21;
                                            if (!doneP)
                                              posP += offNP * FXAA_QUALITY_P21;
                                            if (!doneN)
                                              lumaEndN = FxaaLuma(
                                                  SAMPLE_TEX2D(_texture, posN)
                                                      .rgb);
                                            if (!doneP)
                                              lumaEndP = FxaaLuma(
                                                  SAMPLE_TEX2D(_texture, posP)
                                                      .rgb);
                                            if (!doneN)
                                              doneN = abs(lumaEndN - lumaN) >=
                                                      gradientN;
                                            if (!doneP)
                                              doneP = abs(lumaEndP - lumaN) >=
                                                      gradientN;
                                            if (!doneN && !doneP) {
#endif

#if (FXAA_QUALITY_PS > 22)
                                              if (!doneN)
                                                posN -=
                                                    offNP * FXAA_QUALITY_P22;
                                              if (!doneP)
                                                posP +=
                                                    offNP * FXAA_QUALITY_P22;
                                              if (!doneN)
                                                lumaEndN = FxaaLuma(
                                                    SAMPLE_TEX2D(_texture, posN)
                                                        .rgb);
                                              if (!doneP)
                                                lumaEndP = FxaaLuma(
                                                    SAMPLE_TEX2D(_texture, posP)
                                                        .rgb);
                                              if (!doneN)
                                                doneN = abs(lumaEndN - lumaN) >=
                                                        gradientN;
                                              if (!doneP)
                                                doneP = abs(lumaEndP - lumaN) >=
                                                        gradientN;
                                              if (!doneN && !doneP) {
#endif

#if (FXAA_QUALITY_PS > 23)
                                                if (!doneN)
                                                  posN -=
                                                      offNP * FXAA_QUALITY_P23;
                                                if (!doneP)
                                                  posP +=
                                                      offNP * FXAA_QUALITY_P23;
                                                if (!doneN)
                                                  lumaEndN = FxaaLuma(
                                                      SAMPLE_TEX2D(_texture,
                                                                   posN)
                                                          .rgb);
                                                if (!doneP)
                                                  lumaEndP = FxaaLuma(
                                                      SAMPLE_TEX2D(_texture,
                                                                   posP)
                                                          .rgb);
                                                if (!doneN)
                                                  doneN =
                                                      abs(lumaEndN - lumaN) >=
                                                      gradientN;
                                                if (!doneP)
                                                  doneP =
                                                      abs(lumaEndP - lumaN) >=
                                                      gradientN;
                                                if (!doneN && !doneP) {
#endif

#if (FXAA_QUALITY_PS > 24)
                                                  if (!doneN)
                                                    posN -= offNP *
                                                            FXAA_QUALITY_P24;
                                                  if (!doneP)
                                                    posP += offNP *
                                                            FXAA_QUALITY_P24;
                                                  if (!doneN)
                                                    lumaEndN = FxaaLuma(
                                                        SAMPLE_TEX2D(_texture,
                                                                     posN)
                                                            .rgb);
                                                  if (!doneP)
                                                    lumaEndP = FxaaLuma(
                                                        SAMPLE_TEX2D(_texture,
                                                                     posP)
                                                            .rgb);
                                                  if (!doneN)
                                                    doneN =
                                                        abs(lumaEndN - lumaN) >=
                                                        gradientN;
                                                  if (!doneP)
                                                    doneP =
                                                        abs(lumaEndP - lumaN) >=
                                                        gradientN;
                                                  if (!doneN && !doneP) {
#endif

#if (FXAA_QUALITY_PS > 25)
                                                    if (!doneN)
                                                      posN -= offNP *
                                                              FXAA_QUALITY_P25;
                                                    if (!doneP)
                                                      posP += offNP *
                                                              FXAA_QUALITY_P25;
                                                    if (!doneN)
                                                      lumaEndN = FxaaLuma(
                                                          SAMPLE_TEX2D(_texture,
                                                                       posN)
                                                              .rgb);
                                                    if (!doneP)
                                                      lumaEndP = FxaaLuma(
                                                          SAMPLE_TEX2D(_texture,
                                                                       posP)
                                                              .rgb);
                                                    if (!doneN)
                                                      doneN = abs(lumaEndN -
                                                                  lumaN) >=
                                                              gradientN;
                                                    if (!doneP)
                                                      doneP = abs(lumaEndP -
                                                                  lumaN) >=
                                                              gradientN;
                                                    if (!doneN && !doneP) {
#endif

#if (FXAA_QUALITY_PS > 26)
                                                      if (!doneN)
                                                        posN -=
                                                            offNP *
                                                            FXAA_QUALITY_P26;
                                                      if (!doneP)
                                                        posP +=
                                                            offNP *
                                                            FXAA_QUALITY_P26;
                                                      if (!doneN)
                                                        lumaEndN = FxaaLuma(
                                                            SAMPLE_TEX2D(
                                                                _texture, posN)
                                                                .rgb);
                                                      if (!doneP)
                                                        lumaEndP = FxaaLuma(
                                                            SAMPLE_TEX2D(
                                                                _texture, posP)
                                                                .rgb);
                                                      if (!doneN)
                                                        doneN = abs(lumaEndN -
                                                                    lumaN) >=
                                                                gradientN;
                                                      if (!doneP)
                                                        doneP = abs(lumaEndP -
                                                                    lumaN) >=
                                                                gradientN;
                                                      if (!doneN && !doneP) {
#endif

#if (FXAA_QUALITY_PS > 27)
                                                        if (!doneN)
                                                          posN -=
                                                              offNP *
                                                              FXAA_QUALITY_P27;
                                                        if (!doneP)
                                                          posP +=
                                                              offNP *
                                                              FXAA_QUALITY_P27;
                                                        if (!doneN)
                                                          lumaEndN = FxaaLuma(
                                                              SAMPLE_TEX2D(
                                                                  _texture,
                                                                  posN)
                                                                  .rgb);
                                                        if (!doneP)
                                                          lumaEndP = FxaaLuma(
                                                              SAMPLE_TEX2D(
                                                                  _texture,
                                                                  posP)
                                                                  .rgb);
                                                        if (!doneN)
                                                          doneN = abs(lumaEndN -
                                                                      lumaN) >=
                                                                  gradientN;
                                                        if (!doneP)
                                                          doneP = abs(lumaEndP -
                                                                      lumaN) >=
                                                                  gradientN;
                                                        if (!doneN && !doneP) {
#endif

#if (FXAA_QUALITY_PS > 28)
                                                          if (!doneN)
                                                            posN -=
                                                                offNP *
                                                                FXAA_QUALITY_P28;
                                                          if (!doneP)
                                                            posP +=
                                                                offNP *
                                                                FXAA_QUALITY_P28;
                                                          if (!doneN)
                                                            lumaEndN = FxaaLuma(
                                                                SAMPLE_TEX2D(
                                                                    _texture,
                                                                    posN)
                                                                    .rgb);
                                                          if (!doneP)
                                                            lumaEndP = FxaaLuma(
                                                                SAMPLE_TEX2D(
                                                                    _texture,
                                                                    posP)
                                                                    .rgb);
                                                          if (!doneN)
                                                            doneN =
                                                                abs(lumaEndN -
                                                                    lumaN) >=
                                                                gradientN;
                                                          if (!doneP)
                                                            doneP =
                                                                abs(lumaEndP -
                                                                    lumaN) >=
                                                                gradientN;
// No closing brace for the last one
#endif

// Close all the nested if blocks
#if (FXAA_QUALITY_PS > 28)
                                                        }
#endif
#if (FXAA_QUALITY_PS > 27)
                                                      }
#endif
#if (FXAA_QUALITY_PS > 26)
                                                    }
#endif
#if (FXAA_QUALITY_PS > 25)
                                                  }
#endif
#if (FXAA_QUALITY_PS > 24)
                                                }
#endif
#if (FXAA_QUALITY_PS > 23)
                                              }
#endif
#if (FXAA_QUALITY_PS > 22)
                                            }
#endif
#if (FXAA_QUALITY_PS > 21)
                                          }
#endif
#if (FXAA_QUALITY_PS > 20)
                                        }
#endif
#if (FXAA_QUALITY_PS > 19)
                                      }
#endif
#if (FXAA_QUALITY_PS > 18)
                                    }
#endif
#if (FXAA_QUALITY_PS > 17)
                                  }
#endif
#if (FXAA_QUALITY_PS > 16)
                                }
#endif
#if (FXAA_QUALITY_PS > 15)
                              }
#endif
#if (FXAA_QUALITY_PS > 14)
                            }
#endif
#if (FXAA_QUALITY_PS > 13)
                          }
#endif
#if (FXAA_QUALITY_PS > 12)
                        }
#endif
#if (FXAA_QUALITY_PS > 11)
                      }
#endif
#if (FXAA_QUALITY_PS > 10)
                    }
#endif
#if (FXAA_QUALITY_PS > 9)
                  }
#endif
#if (FXAA_QUALITY_PS > 8)
                }
#endif
#if (FXAA_QUALITY_PS > 7)
              }
#endif
#if (FXAA_QUALITY_PS > 6)
            }
#endif
#if (FXAA_QUALITY_PS > 5)
          }
#endif
#if (FXAA_QUALITY_PS > 4)
        }
#endif
#if (FXAA_QUALITY_PS > 3)
      }
#endif
#if (FXAA_QUALITY_PS > 2)
    }
#endif
#if (FXAA_QUALITY_PS > 1)
  }
#endif
#if (FXAA_QUALITY_PS > 0)
}
#endif

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