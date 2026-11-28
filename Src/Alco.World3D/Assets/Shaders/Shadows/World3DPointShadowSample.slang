#language slang 2025
module World3DPointShadowSample;

import Core;

public struct World3DPointShadowFrameGpu
{
    public GPUBufferView Pages;
    public GPUTextureId Tier0;
    public GPUTextureId Tier1;
    public GPUTextureId Tier2;
    public uint Reserved;
};

public struct World3DPointShadowPageGpu
{
    public uint Tier;
    public uint FirstLayer;
    public uint UsableFaces;
    public float Near;
    public float Radius;
    public float Fade0;
    public float Fade1;
    public float Fade2;
    public float Fade3;
    public float Fade4;
    public float Fade5;
    public uint Reserved;
};

static const float2 PointShadowPoisson[12] = {
    float2(-0.326, -0.406), float2(-0.840, -0.074), float2(-0.696, 0.457),
    float2(-0.203,  0.621), float2( 0.962, -0.195), float2( 0.473, -0.480),
    float2( 0.519,  0.767), float2( 0.185, -0.893), float2( 0.507, 0.064),
    float2( 0.896,  0.412), float2(-0.322, -0.933), float2(-0.792, -0.598),
};

// The matching 90-degree capture bases use +Z up, except +X up on the two Z faces.
uint pointShadowProject(float3 direction, out float2 uv)
{
    float3 axis = abs(direction);
    float2 rightUp;
    float z;
    uint face;
    if (axis.x >= axis.y && axis.x >= axis.z)
    {
        face = direction.x >= 0.0 ? 0u : 1u;
        rightUp = float2(direction.x >= 0.0 ? direction.y : -direction.y, direction.z);
        z = axis.x;
    }
    else if (axis.y >= axis.z)
    {
        face = direction.y >= 0.0 ? 2u : 3u;
        rightUp = float2(direction.y >= 0.0 ? -direction.x : direction.x, direction.z);
        z = axis.y;
    }
    else
    {
        face = direction.z >= 0.0 ? 4u : 5u;
        rightUp = float2(direction.z >= 0.0 ? -direction.y : direction.y, direction.x);
        z = axis.z;
    }
    uv = rightUp * (float2(0.5, -0.5) / z) + 0.5;
    return face;
}

float pointShadowFade(World3DPointShadowPageGpu page, uint face)
{
    switch (face)
    {
        case 0u: return page.Fade0;
        case 1u: return page.Fade1;
        case 2u: return page.Fade2;
        case 3u: return page.Fade3;
        case 4u: return page.Fade4;
        default: return page.Fade5;
    }
}

// An absent tap, including an uncleared layer, never contributes a blocker.
bool pointShadowTryReadBlocker(Texture2DArray<float> depthTexture, World3DPointShadowPageGpu page,
    int resolution, float3 direction, float compareDistance, out float blockerDistance, out float fade)
{
    blockerDistance = 0.0;
    fade = 0.0;
    float2 uv;
    uint face = pointShadowProject(direction, uv);
    if ((page.UsableFaces & (1u << face)) == 0u)
        return false;

    fade = pointShadowFade(page, face);
    if (!(fade > 0.0))
        return false;

    int2 pixel = clamp(int2(uv * float(resolution)), int2(0), int2(resolution - 1));
    float depth = depthTexture.Load(int4(pixel, int(page.FirstLayer + face), 0));
    if (!(depth >= 0.0 && depth < 1.0))
        return false;

    // D32 stores projected face-axis depth, not radial distance. Recover distance along
    // the texel center ray so blocker lengths agree across faces and in the PCSS ratio.
    float a = page.Radius / (page.Radius - page.Near);
    float faceDepth = a * page.Near / (a - depth);
    float2 faceNdc = (float2(pixel) + 0.5) * (2.0 / float(resolution)) - 1.0;
    blockerDistance = faceDepth * sqrt(1.0 + dot(faceNdc, faceNdc));
    return blockerDistance < compareDistance;
}

/// Returns the fraction of light visible from an eye-relative light-to-receiver vector.
public float world3d_point_shadow_visibility(World3DPointShadowFrameGpu frame, uint shadowIndexPlusOne,
    float3 lightToReceiver, float3 normal, float sourceRadius)
{
    if (shadowIndexPlusOne == 0u || shadowIndexPlusOne > 72u || frame.Pages.Index == 0u)
        return 1.0;

    World3DPointShadowPageGpu page = gpu_load<World3DPointShadowPageGpu>(frame.Pages, shadowIndexPlusOne - 1u);
    float3 axis = abs(lightToReceiver);
    if (page.UsableFaces == 0u || !(max(axis.x, max(axis.y, axis.z)) > page.Near))
        return 1.0;

    float receiverDistance = length(lightToReceiver);
    if (!(receiverDistance < page.Radius))
        return 1.0;

    uint textureIndex = page.Tier == 0u ? frame.Tier0.index : page.Tier == 1u ? frame.Tier1.index : frame.Tier2.index;
    if (page.Tier > 2u || textureIndex == 0u)
        return 1.0;
    Texture2DArray<float> depthTexture = gpu_texture<Texture2DArray<float>>(gpu_nonuniform_index(textureIndex));

    int resolution = 64 << page.Tier;
    float texelSlope = 2.0 / float(resolution);
    float3 direction = lightToReceiver / receiverDistance;
    float ndotL = saturate(dot(normal, -direction));
    float slope = sqrt(max(1.0 - ndotL * ndotL, 0.0)) / max(ndotL, 0.25);
    float compareDistance = receiverDistance * (1.0 - texelSlope * (0.35 + 0.35 * slope));
    if (compareDistance <= page.Near)
        return 1.0;

    // Build the filter in direction space, not in a face's UV chart. Every sample chooses
    // its own layer instead of clamping its direction at a cube seam.
    float signZ = direction.z >= 0.0 ? 1.0 : -1.0;
    float basis = -1.0 / (signZ + direction.z);
    float xy = direction.x * direction.y * basis;
    float3 tangent = float3(1.0 + signZ * direction.x * direction.x * basis, signZ * xy, -signZ * direction.x);
    float3 bitangent = float3(xy, signZ + direction.y * direction.y * basis, -direction.y);
    float filterSlope = 1.5 * texelSlope;
    if (page.Tier == 2u && sourceRadius > 0.0)
    {
        float maxSlope = 24.0 * texelSlope;
        float searchSlope = clamp(sourceRadius / receiverDistance, filterSlope, maxSlope);
        float blockerSum = 0.0;
        float blockerWeight = 0.0;
        [unroll]
        for (int i = 0; i < 12; ++i)
        {
            float2 offset = PointShadowPoisson[i];
            float3 ray = direction + (tangent * offset.x + bitangent * offset.y) * searchSlope;
            float blockerDistance;
            float weight;
            if (pointShadowTryReadBlocker(depthTexture, page, resolution, ray, compareDistance, blockerDistance, weight))
            {
                blockerSum += blockerDistance * weight;
                blockerWeight += weight;
            }
        }
        if (blockerWeight > 0.0)
        {
            float blockerDistance = blockerSum / blockerWeight;
            float penumbraWorld = sourceRadius * max(receiverDistance - blockerDistance, 0.0) / blockerDistance;
            filterSlope = clamp(penumbraWorld / receiverDistance, filterSlope, maxSlope);
        }
    }

    float occlusion = 0.0;
    [unroll]
    for (int i = 0; i < 12; ++i)
    {
        float2 offset = PointShadowPoisson[i];
        float3 ray = direction + (tangent * offset.x + bitangent * offset.y) * filterSlope;
        float blockerDistance;
        float fade;
        if (pointShadowTryReadBlocker(depthTexture, page, resolution, ray, compareDistance, blockerDistance, fade))
            occlusion += fade;
    }
    return saturate(1.0 - occlusion / 12.0);
}
