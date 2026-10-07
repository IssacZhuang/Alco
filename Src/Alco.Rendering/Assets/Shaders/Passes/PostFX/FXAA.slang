// file "FXAA.slang" - module "FXAA"
//
// Resources are grouped as ParameterBlocks — the compiler assigns each block
// its own descriptor set in declaration order; members resolve by bare field name.

#language slang 2025
module FXAA;

import AlcoRendering_Core;

public struct PassParams
{
    public Texture2D texture;
    // Sampleable scene depth for the depth-aware modes. The field survives in
    // every specialization's parameter layout (ParameterBlock fields are not
    // pruned per specialization), so the material binds it in all modes; the
    // luma mode's dead code drops the loads.
    public DepthTexture2D depthTexture;
};

public ParameterBlock<PassParams> pass;

// FXAA configuration constants
public struct FxaaData
{
    public float2 invFrameSize;       // 1.0 / FrameSize
    public float threshold;           // Edge detection threshold (0.063-0.333)
    public float subpix;              // Subpixel aliasing removal amount (0-1)
    public float depthThresholdMin;   // Depth edge absolute floor (depth modes)
    public float depthThresholdRel;   // Depth edge fraction of center depth (depth modes)
};

public ParameterBlock<FxaaData> fxaaData;


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

// The C# owner requests one specialized shader per quality level; after
// specialization each selector function folds to its single matching table.

// 0 = FXAA_QUALITY_LOW    - 4 steps,  fastest performance
// 1 = FXAA_QUALITY_MEDIUM - 8 steps,  balanced
// 2 = FXAA_QUALITY_HIGH   - 12 steps, high quality
// 3 = FXAA_QUALITY_ULTRA  - 29 steps, maximum quality
//
// The mode axis selects the edge detector:
// 0 = FXAA_MODE_LUMA           - classic FXAA 3.11 luma-only detection
// 1 = FXAA_MODE_DEPTH_ASSISTED - depth discontinuities rescue edges the luma
//                                gate rejects and steer the edge walk on
//                                geometric edges; luma remains the fallback
// 2 = FXAA_MODE_DEPTH_ONLY     - only depth-detected geometric edges are
//                                anti-aliased (shader/specular edges pass
//                                through untouched)
//
// Integer-variant branches use if statements rather than ternaries — a
// ternary condition that const-folds to true from an int generic comparison
// crashed slang 2026.16 natively (fixed in 2026.19; see
// GenericValueParameterTest).

static const float sampleDistancesLow[4] = {
  1.5,
  3.0,
  12.0,
  12.0
};

static const float sampleDistancesMedium[8] = {
  1.0,
  1.5,
  2.0,
  2.0,
  2.0,
  2.0,
  4.0,
  8.0
};

static const float sampleDistancesHigh[12] = {
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

static const float sampleDistancesUltra[29] = {
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

/// The number of edge-search steps of one quality preset (0=Low..3=Ultra).
int fxaaNumSamples<let quality : int>() {
  if (quality == 0) { return 4; }
  else if (quality == 1) { return 8; }
  else if (quality == 3) { return 29; }
  else { return 12; }
}

/// The i-th search-step distance of one quality preset.
float fxaaSampleDistance<let quality : int>(int i) {
  if (quality == 0) { return sampleDistancesLow[i]; }
  else if (quality == 1) { return sampleDistancesMedium[i]; }
  else if (quality == 3) { return sampleDistancesUltra[i]; }
  else { return sampleDistancesHigh[i]; }
}

/*============================================================================
                        FXAA 3.11 ALGORITHM
============================================================================*/

// Convert rgb to luma (perceptual luminance)
float fxaaLuma(float3 rgb) {
  return rgb.y * (0.587 / 0.299) + rgb.x;
}

/*============================================================================
                    DEPTH EDGE DETECTION (mode != 0)
============================================================================*/

// A neighbor lies across a geometric edge when its depth differs from the
// center by more than a floor plus a relative fraction of the center depth
// (both tunable through the fxaaData block). Raw depth compares suffice under
// both conventions the engine renders with: orthographic forward (near=0,
// far=1, sky=1) and reversed-infinite (near=1, sky=0) — both are monotonic
// and the sky clear value simply sits at the far end, so the relative test
// needs no linearization.

struct FxaaDepthEdge {
  bool isEdge;
  bool horzSpan; // the discontinuity implies a horizontal span (N/S gradient)
};

// Loads the 4-neighborhood of scene depth around the pixel and reports the
// geometric edge. Mirrors the luma edgeHorz/edgeVert semantics: the N/S depth
// gradient marks a horizontal span, the W/E gradient a vertical one.
FxaaDepthEdge fxaaDetectDepthEdge(float2 pos, float2 rcpFrame) {
  int2 pixel = int2(pos / rcpFrame); // texel centers land on integer pixels
  float dM = pass.depthTexture.Load(int3(pixel, 0));
  float dN = pass.depthTexture.Load(int3(pixel + int2(0, -1), 0));
  float dS = pass.depthTexture.Load(int3(pixel + int2(0, 1), 0));
  float dW = pass.depthTexture.Load(int3(pixel + int2(-1, 0), 0));
  float dE = pass.depthTexture.Load(int3(pixel + int2(1, 0), 0));

  float neighborDiff = max(abs(dN - dM), max(abs(dS - dM), max(abs(dW - dM), abs(dE - dM))));
  float threshold = max(fxaaData.depthThresholdMin, abs(dM) * fxaaData.depthThresholdRel);

  FxaaDepthEdge edge;
  edge.isEdge = neighborDiff > threshold;
  float depthHorz = abs(dN - dM) + abs(dS - dM);
  float depthVert = abs(dW - dM) + abs(dE - dM);
  edge.horzSpan = depthHorz >= depthVert;
  return edge;
}

float4 fxaaPixelShader<let quality : int, let mode : int>(float2 pos,     // Pixel position
                                                         float2 rcpFrame // 1.0 / screen size
) {
  // FXAA algorithm parameters
  float fxaaQualitySubpix = fxaaData.subpix; // Subpixel aliasing removal amount
  float fxaaQualityEdgeThreshold = fxaaData.threshold; // Edge detection threshold
  float fxaaQualityEdgeThresholdMin = 0.0625; // Minimum threshold

  /*--------------------------------------------------------------------------*/
  // LOCAL CONTRAST CHECK
  /*--------------------------------------------------------------------------*/

  // Sample center and surrounding pixels
  float3 rgbN = pass.texture.Sample(samplers.linearClamp, pos + float2(0.0, -1.0) * rcpFrame).rgb;
  float3 rgbW = pass.texture.Sample(samplers.linearClamp, pos + float2(-1.0, 0.0) * rcpFrame).rgb;
  float3 rgbM = pass.texture.Sample(samplers.linearClamp, pos).rgb;
  float3 rgbE = pass.texture.Sample(samplers.linearClamp, pos + float2(1.0, 0.0) * rcpFrame).rgb;
  float3 rgbS = pass.texture.Sample(samplers.linearClamp, pos + float2(0.0, 1.0) * rcpFrame).rgb;

  // Convert to luma
  float lumaN = fxaaLuma(rgbN);
  float lumaW = fxaaLuma(rgbW);
  float lumaM = fxaaLuma(rgbM);
  float lumaE = fxaaLuma(rgbE);
  float lumaS = fxaaLuma(rgbS);

  // Find the maximum and minimum luma around this pixel
  float rangeMin = min(lumaM, min(min(lumaN, lumaW), min(lumaS, lumaE)));
  float rangeMax = max(lumaM, max(max(lumaN, lumaW), max(lumaS, lumaE)));
  float range = rangeMax - rangeMin;

  // Geometric edge around this pixel (depth modes only; folds away for luma).
  bool depthEdge = false;
  bool depthHorzSpan = false;
  if (mode != 0) {
    FxaaDepthEdge edge = fxaaDetectDepthEdge(pos, rcpFrame);
    depthEdge = edge.isEdge;
    depthHorzSpan = edge.horzSpan;
  }

  /*--------------------------------------------------------------------------*/
  // LOCAL CONTRAST CHECK
  /*--------------------------------------------------------------------------*/

  // Which pixels proceed to edge work, per mode:
  // luma (0)          — local luma contrast must clear the luma gate;
  // depth-assisted(1) — a depth edge rescues pixels the luma gate rejects,
  //                     but only when the edge is visible at all (range above
  //                     the absolute floor): same-colored geometry at
  //                     different depths must stay untouched;
  // depth-only (2)    — the geometric edge alone decides, same visibility
  //                     floor, so shader/specular aliasing passes through.
  bool processEdge = false;
  if (mode == 0) {
    processEdge = range >= max(fxaaQualityEdgeThresholdMin, rangeMax * fxaaQualityEdgeThreshold);
  }
  if (mode == 1) {
    processEdge = range >= max(fxaaQualityEdgeThresholdMin, rangeMax * fxaaQualityEdgeThreshold) ||
                  (depthEdge && range >= fxaaQualityEdgeThresholdMin);
  }
  if (mode == 2) {
    processEdge = depthEdge && range >= fxaaQualityEdgeThresholdMin;
  }
  if (!processEdge) {
    return float4(rgbM, 1.0);
  }

  /*--------------------------------------------------------------------------*/
  // SUBPIXEL ALIASING TEST
  /*--------------------------------------------------------------------------*/

  // Sample the corners
  float3 rgbL = rgbN + rgbW + rgbM + rgbE + rgbS;

  float3 rgbNW =
      pass.texture.Sample(samplers.linearClamp, pos + float2(-1.0, -1.0) * rcpFrame).rgb;
  float3 rgbNE = pass.texture.Sample(samplers.linearClamp, pos + float2(1.0, -1.0) * rcpFrame).rgb;
  float3 rgbSW = pass.texture.Sample(samplers.linearClamp, pos + float2(-1.0, 1.0) * rcpFrame).rgb;
  float3 rgbSE = pass.texture.Sample(samplers.linearClamp, pos + float2(1.0, 1.0) * rcpFrame).rgb;

  rgbL += (rgbNW + rgbNE + rgbSW + rgbSE);
  rgbL *= (1.0 / 9.0);

  float lumaNW = fxaaLuma(rgbNW);
  float lumaNE = fxaaLuma(rgbNE);
  float lumaSW = fxaaLuma(rgbSW);
  float lumaSE = fxaaLuma(rgbSE);

  // Compute the subpixel offset
  float lumaL = (lumaN + lumaW + lumaE + lumaS) * 0.25;
  float rangeL = abs(lumaL - lumaM);
  float blendL = max(0.0, (rangeL / range) - 0.25) * 1.333;
  // Geometric edges keep their hard silhouette: the local-average blend
  // exists for subpixel shader detail, and on a true depth discontinuity it
  // only softens the silhouette the directional resample just recentered.
  if (mode != 0) {
    if (depthEdge) {
      blendL = 0.0;
    }
  }
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
  // On a geometric edge the depth gradient is the trustworthy direction —
  // exactly the low-contrast silhouettes whose luma orientation is noise.
  if (mode != 0) {
    if (depthEdge) {
      horzSpan = depthHorzSpan;
    }
  }

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

  // Variable step search using the quality preset's sample distances
  [unroll]
  for (int i = 0; i < fxaaNumSamples<quality>(); i++) {
    if (!doneN) {
      posN -= offNP * fxaaSampleDistance<quality>(i);
      lumaEndN = fxaaLuma(pass.texture.Sample(samplers.linearClamp, posN).rgb);
      doneN = abs(lumaEndN - lumaN) >= gradientN;
    }

    if (!doneP) {
      posP += offNP * fxaaSampleDistance<quality>(i);
      lumaEndP = fxaaLuma(pass.texture.Sample(samplers.linearClamp, posP).rgb);
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
    pass.texture.Sample(samplers.linearClamp, float2(pos.x + (horzSpan ? 0.0 : subPixelOffset),
                                  pos.y + (horzSpan ? subPixelOffset : 0.0)))
        .rgb;

// Blend with subpixel anti-aliasing
return float4(lerp(rgbF, rgbL, blendL), 1.0);
}

[shader("fragment")] float4 MainPS<let quality : int, let mode : int>(V2F input) : SV_TARGET {
  return fxaaPixelShader<quality, mode>(input.uv, fxaaData.invFrameSize);
}