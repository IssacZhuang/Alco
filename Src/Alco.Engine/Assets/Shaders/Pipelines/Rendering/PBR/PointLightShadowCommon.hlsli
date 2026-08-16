#ifndef POINT_LIGHT_SHADOW_COMMON_HLSLI
#define POINT_LIGHT_SHADOW_COMMON_HLSLI

// Shared uniform of the point light shadow screen chain (PointLightShadowTrace /
// Resolve / Upsample). The layout must match RGNode_PointLightShadow's private
// PointLightShadowData struct on the C# side exactly. Include after
// Shaders/Libs/Core.hlsli.

DEFINE_UNIFORM(0, _data)
{
    float4x4 invViewProjection;
    float4x4 viewProjectionPrev; // previous frame's view-projection (temporal reprojection)
    float4 cameraPosition;       // xyz = world-space camera position
    float4 plParams;             // x=numPointLights y=lightRadius z=traceWidth w=traceHeight
    float4 plParams2;            // x=gbufferWidth y=gbufferHeight z=frameIndex w=historyValid
    float4 plParams3;            // x=1/faceSize y=1/atlasWidth z=1/atlasHeight w=maxPenumbraTexels
};

#endif // POINT_LIGHT_SHADOW_COMMON_HLSLI
