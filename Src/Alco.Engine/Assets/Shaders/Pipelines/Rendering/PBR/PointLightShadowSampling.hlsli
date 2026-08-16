#ifndef POINT_LIGHT_SHADOW_SAMPLING_HLSLI
#define POINT_LIGHT_SHADOW_SAMPLING_HLSLI

// Analytic cube-face projection and PCSS visibility sampling against the point
// light shadow atlas. The including shader MUST declare, BEFORE including this
// file (any bind set):
//   DEFINE_TEX2D_DEPTH_SAMPLE(index, _plShadowAtlas);  // comparison PCF taps
//   DEFINE_TEX2D_DEPTH(index, _plShadowAtlasLoad);     // raw loads (blocker search)
// and include Shaders/Libs/Core.hlsli first. The per-light metadata buffer is
// declared by this header itself (bind group overridable via
// PLS_SHADOW_INFO_SET, set 0 by default). The face math mirrors
// PointLightShadowMath on the C# side (which also builds the folded matrices
// PointLightShadowDepth.hlsl rasterizes with), so the sampling side and the
// rendering side agree by construction (asserted by TestPointLightShadow).

// Per-light shadow metadata uploaded by RGNode_PointLightShadow.
struct PointLightShadowInfo
{
    float4 slotNearFar; // x = slot index (-1 = unshadowed), y = near plane, z = far plane, w unused
};

#ifndef PLS_SHADOW_INFO_SET
#define PLS_SHADOW_INFO_SET 0
#endif
DEFINE_STORAGE(PLS_SHADOW_INFO_SET, PointLightShadowInfo, _plShadowInfo);

// Cube-face bases in engine axis convention (X+ forward, Y+ right, Z+ up).
// Index order: 0=+X, 1=-X, 2=+Y, 3=-Y, 4=+Z, 5=-Z. Pairs with the static
// arrays in PointLightShadowMath.cs — keep both in sync.
static const float3 PlsFaceForwards[6] =
{
    float3( 1.0, 0.0, 0.0), float3(-1.0, 0.0, 0.0),
    float3( 0.0, 1.0, 0.0), float3( 0.0, -1.0, 0.0),
    float3( 0.0, 0.0, 1.0), float3( 0.0, 0.0, -1.0)
};
static const float3 PlsFaceRights[6] =
{
    float3( 0.0, 1.0, 0.0), float3( 0.0, -1.0, 0.0),
    float3(-1.0, 0.0, 0.0), float3( 1.0, 0.0, 0.0),
    float3( 1.0, 0.0, 0.0), float3(-1.0, 0.0, 0.0)
};
static const float3 PlsFaceUps[6] =
{
    float3( 0.0, 0.0, 1.0), float3( 0.0, 0.0, 1.0),
    float3( 0.0, 0.0, 1.0), float3( 0.0, 0.0, 1.0),
    float3( 0.0, 1.0, 0.0), float3( 0.0, 1.0, 0.0)
};

// Atlas layout: 4 slot cells per row, each cell a 3x2 grid of faces. Pairs with
// RGNode_PointLightShadow.SlotsPerRow — keep both in sync.
static const int PlsSlotsPerRow = 4;

// 12-tap Poisson disk (first 8 taps are reused for the blocker search).
static const float2 PlsPoissonDisk[12] =
{
    float2(-0.326212, -0.405805), float2(-0.840144, -0.073580),
    float2(-0.695914,  0.457137), float2(-0.203345,  0.620716),
    float2( 0.962340, -0.194980), float2( 0.473434, -0.480026),
    float2( 0.019182, -0.942415), float2( 0.184280,  0.039386),
    float2(-0.174900,  0.191360), float2( 0.892060,  0.402714),
    float2( 0.397870,  0.866330), float2(-0.834930, -0.701010)
};

// Projected (0..1) depth of a face-space forward distance. Matches the
// perspective convention of the CPU-side face matrices (0..1 depth, LH).
float PlsLinearToProjectedDepth(float z, float nearPlane, float farPlane)
{
    return farPlane * (z - nearPlane) / (z * (farPlane - nearPlane));
}

// Face-space forward distance of a projected (0..1) depth value.
float PlsProjectedDepthToLinear(float projected, float nearPlane, float farPlane)
{
    return nearPlane * farPlane / (farPlane - projected * (farPlane - nearPlane));
}

// Interleaved Gradient Noise (Jimenez 2014) with a per-tap golden-ratio offset
// so the blocker search and the PCF rotation decorrelate.
float PlsInterleavedGradientNoise(float2 pix, float offset)
{
    return frac(52.9829189 * frac(dot(pix, float2(0.06711056, 0.00583715)) + offset));
}

// Percentage-closer soft shadow visibility of one point light at a receiver.
// <paramref name="L"/> is the unit vector receiver -> light,
// <paramref name="atlasParams"/> packs x=1/faceSize y=1/atlasWidth z=1/atlasHeight
// w=maxPenumbraTexels. Returns visibility in [0,1]; unslotted lights and
// receivers behind the near plane are fully lit.
float SamplePointLightVisibility(
    float3 worldPosition,
    float3 N,
    float3 L,
    float3 lightPosition,
    float distanceToLight,
    float4 slotNearFar,
    float4 atlasParams,
    float lightRadius,
    float2 noisePixel)
{
    int slot = (int)slotNearFar.x;
    if (slot < 0)
    {
        return 1.0;
    }
    float nearPlane = slotNearFar.y;
    float farPlane = slotNearFar.z;
    float faceSize = rcp(atlasParams.x);
    float atlasWidth = rcp(atlasParams.y);
    float atlasHeight = rcp(atlasParams.z);

    // Normal-offset bias: one face texel spans 2*z/faceSize world units at the
    // receiver distance (90° FOV), scaled like the CSM normal offset.
    float texelWorld = 2.0 * distanceToLight / faceSize;
    float3 p = (worldPosition + N * (texelWorld * 1.5)) - lightPosition;

    // Dominant face (largest offset component): guarantees |ndc| <= 1.
    float3 absP = abs(p);
    int axis = (absP.x >= absP.y && absP.x >= absP.z) ? 0 : (absP.y >= absP.z ? 1 : 2);
    int face = axis * 2 + (dot(p, PlsFaceForwards[axis * 2]) >= 0.0 ? 0 : 1);
    float3 forward = PlsFaceForwards[face];
    float3 right = PlsFaceRights[face];
    float3 up = PlsFaceUps[face];

    float zv = dot(p, forward);
    if (zv <= nearPlane)
    {
        // Receiver at/behind the near plane (inside the emitter housing).
        return 1.0;
    }
    float xv = dot(p, right) / zv;
    float yv = dot(p, up) / zv;
    float2 uvLocal = float2(xv * 0.5 + 0.5, 0.5 - yv * 0.5);

    // Face tile rect: slot cell (3x2 faces) + face offset within the cell.
    float2 origin = float2(
        (float)((slot % PlsSlotsPerRow) * 3 + (face % 3)),
        (float)((slot / PlsSlotsPerRow) * 2 + (face / 3))) * faceSize;
    float2 invAtlas = float2(atlasParams.y, atlasParams.z);
    float2 uvMin = (origin + 0.5) * invAtlas;
    float2 uvMax = (origin + faceSize - 0.5) * invAtlas;
    float2 shadowUV = (origin + uvLocal * faceSize) * invAtlas;

    // ── PCSS stage 1: blocker search over the light's projected disk ──
    // uvLocal units: the face spans 1.0 across 2*z world units.
    float searchRadiusUV = saturate(lightRadius / zv) * 0.5;
    float2 searchScale = searchRadiusUV * faceSize * invAtlas;
    float blockerAngle = PlsInterleavedGradientNoise(noisePixel, 0.61803398) * 6.2831853;
    float blockerSin, blockerCos;
    sincos(blockerAngle, blockerSin, blockerCos);
    float2x2 blockerRotation = float2x2(blockerCos, -blockerSin, blockerSin, blockerCos);

    float blockerSum = 0.0;
    float blockerCount = 0.0;
    [unroll]
    for (int b = 0; b < 8; b++)
    {
        float2 tapUV = clamp(shadowUV + mul(blockerRotation, PlsPoissonDisk[b]) * searchScale, uvMin, uvMax);
        int2 tapTexel = int2(round(tapUV * float2(atlasWidth, atlasHeight)));
        float storedProjected = _plShadowAtlasLoad.Load(int3(tapTexel, 0)).r;
        if (storedProjected >= 0.9999)
        {
            continue; // far plane: no caster
        }
        float storedLinear = PlsProjectedDepthToLinear(storedProjected, nearPlane, farPlane);
        if (storedLinear < zv - texelWorld)
        {
            blockerSum += storedLinear;
            blockerCount += 1.0;
        }
    }
    if (blockerCount < 0.5)
    {
        return 1.0;
    }
    float blockerLinear = blockerSum / blockerCount;

    // ── PCSS stage 2: penumbra from the light's physical radius ──
    float penumbraWorld = lightRadius * (zv - blockerLinear) / max(blockerLinear, 1e-3);
    float penumbraTexels = clamp(penumbraWorld / (2.0 * zv) * faceSize, 1.0, atlasParams.w);
    float2 penumbraScale = penumbraTexels * invAtlas;

    // Slope-scaled comparison bias, converted through the depth convention.
    float NdotL = saturate(dot(N, L));
    float biasWorld = texelWorld * (0.75 + 1.5 * (1.0 - NdotL));
    float compareDepth = PlsLinearToProjectedDepth(zv - biasWorld, nearPlane, farPlane);

    // ── PCSS stage 3: variable-radius Poisson PCF ──
    float pcfAngle = PlsInterleavedGradientNoise(noisePixel, 0.31622776) * 6.2831853;
    float pcfSin, pcfCos;
    sincos(pcfAngle, pcfSin, pcfCos);
    float2x2 pcfRotation = float2x2(pcfCos, -pcfSin, pcfSin, pcfCos);

    float shadow = 0.0;
    [unroll]
    for (int t = 0; t < 12; t++)
    {
        float2 tapUV = clamp(shadowUV + mul(pcfRotation, PlsPoissonDisk[t]) * penumbraScale, uvMin, uvMax);
        shadow += SAMPLE_TEX2D_DEPTH_CMP(_plShadowAtlas, tapUV, compareDepth);
    }
    return shadow * (1.0 / 12.0);
}

#endif // POINT_LIGHT_SHADOW_SAMPLING_HLSLI
