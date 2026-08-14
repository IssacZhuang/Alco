using System.Numerics;

namespace Alco.Rendering;

/// <summary>
/// Per-frame data uploaded to the deferred lighting pass. Layout must match the
/// <c>_data</c> cbuffer in DeferredLighting.hlsl exactly. Assembled by the pipeline
/// from caller-set scene properties (sun direction/color, sky params, GI strength,
/// debug flags) and pipeline-owned data (camera, cascades, viewport).
/// </summary>
public struct DeferredLightingData
{
    /// <summary>Inverse of the camera view-projection matrix.</summary>
    public Matrix4x4 InvViewProjection;
    /// <summary>Sun light view-projection matrix of shadow cascade 0 (nearest).</summary>
    public Matrix4x4 SunViewProjection0;
    /// <summary>Sun light view-projection matrix of shadow cascade 1.</summary>
    public Matrix4x4 SunViewProjection1;
    /// <summary>Sun light view-projection matrix of shadow cascade 2.</summary>
    public Matrix4x4 SunViewProjection2;
    /// <summary>Sun light view-projection matrix of shadow cascade 3 (farthest).</summary>
    public Matrix4x4 SunViewProjection3;
    /// <summary>Camera position in world space (w unused).</summary>
    public Vector4 CameraPosition;
    /// <summary>Normalized direction the sun light travels (w unused).</summary>
    public Vector4 SunDirection;
    /// <summary>Sun linear color (rgb) and intensity (w).</summary>
    public Vector4 SunColorAndIntensity;
    /// <summary>Atmosphere parameters: x=rayleighScale, y=mieScale, z=miePhaseG, w=exposure (see Atmosphere.hlsli).</summary>
    public Vector4 SkyParams;
    /// <summary>Atmosphere parameters: x=starIntensity, y=nightFloor, z=sunRadianceScale, w=ambientFloor (minimum hemisphere ambient multiplier).</summary>
    public Vector4 SkyParams2;
    /// <summary>Azimuthally filtered physical-sky radiance at the horizon.</summary>
    public Vector4 SkyHorizonColor;
    /// <summary>Filtered physical-sky radiance at the zenith.</summary>
    public Vector4 SkyZenithColor;
    /// <summary>x=shadowEnabled y=numPointLights z=shadowMapSize w=sunDiscEnabled.</summary>
    public Vector4 Params;
    /// <summary>View-distance end boundary of each cascade; beyond w there is no shadow.</summary>
    public Vector4 CascadeSplits;
    /// <summary>World units per shadow texel of each cascade (for the normal-offset bias).</summary>
    public Vector4 CascadeTexelSizes;
    /// <summary>x=cascadeDebugTint, y=shadowFactorView, z=unused, w=aoDebugView.</summary>
    public Vector4 Params2;
    /// <summary>xy=render target size in pixels (filled by the pipeline).</summary>
    public Vector4 ViewportSize;
    /// <summary>x=giEnabled, y=giDiffuseStrength, z=giSpecularStrength, w=giDebugView (0=off 1=diffuse 2=specular 3=visibility).</summary>
    public Vector4 Params3;
    /// <summary>x=sunDiscSize (cosine angular threshold, higher = smaller disc), y=sunDiscBrightness (HDR visual brightness independent of lighting intensity), z=1/GI trace width, w=1/GI trace height (filled by the pipeline, 0 when GI is off).</summary>
    public Vector4 Params4;
    /// <summary>Volumetric light params: x=enabled(&gt;0), y=fogDensity, z=heightScaleHeight (height-falloff model, ignored for constant), w=phaseG (Henyey-Greenstein anisotropy).</summary>
    public Vector4 VLParams;
    /// <summary>Volumetric cloud shadow params: x=strength (0=off), y=shadow plane altitude (meters, mid-slab), z=coverage texture half extent (meters), w=enabled(&gt;0). The coverage texture itself is bound to the lighting material's _cloudShadow slot by the clouds plugin.</summary>
    public Vector4 CloudShadow;
}
