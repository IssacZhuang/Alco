// file "GpuTrail2D.slang" - module "GpuTrail2D"
//
// The built-in 2D trail render pass TEMPLATE: draws the GPU-resident ribbon
// machinery of GpuTrailSystem2D (AlcoRendering_Trails2D - shared point ring
// buffers, multi-draw-indirect over a static quad-strip index pattern).
// Both entries are generic over the trail's surface (ITrailSurface,
// AlcoRendering_TrailSurface): the material compiler composes this module
// with the trail material's surface module, so the visual comes from the
// .amat - the built-in default surface (TrailSurfaceDefault)
// shades the color gradient with a soft across-ribbon edge and leaves the
// vertex stage untouched (the getVertexAdjustment hook's default).
//
// The pass owns the resource contract the trail system binds: the "camera"
// block (auto-bound view-projection), the "trailScene" block (the trail
// point/parameter ring buffers) and the "trailGlobals" block (the renderer
// clock). The surface module owns the material resources (textures and
// [MaterialParams] values). The template computes the ribbon geometry - point
// fetch, validity, aging, the width0 -> width1 envelope, the fade in/out
// alpha envelope and the projection - so a surface shapes the look, not the
// machinery.

#language slang 2025
module GpuTrail2D;

import AlcoRendering_Trails2D;
import AlcoRendering_TrailSurface;

public struct V2F
{
    public float4 position : SV_POSITION;
    public float2 uv : TEXCOORD0;      // x: arc length, y: -1..1 across the ribbon
    public float4 color : COLOR0;      // the color gradient, envelope alpha applied
    public float age01 : TEXCOORD1;   // the normalized point age in [0, 1]
    public float age : TEXCOORD2;     // seconds since the point was emitted
    public float seed : TEXCOORD3;    // the trail's random seed
    public float4 userData : TEXCOORD4; // the trail's custom data channel
    public float4 data : TEXCOORD5;   // the surface hook's private VS -> FS channel
    public float3 worldPosition : TEXCOORD6; // final ribbon position; z is sorting depth
};

// The transparent layer output: a single premultiplied-alpha slot. Trail
// occluders (smoke) resolve in the map's lighting composite over both the lit
// scene color and the emission buffer.

public struct CameraParams
{
    public float4x4 viewProjection;
};

public ParameterBlock<CameraParams> camera;

// The GpuTrailSystem2D buffers (the member names are the binding contract).
public struct TrailSceneParams
{
    public RWStructuredBuffer<TrailPoint2D> trailPoints;
    public RWStructuredBuffer<TrailParams2D> trailParams;
};

public ParameterBlock<TrailSceneParams> trailScene;

// The renderer's per-frame globals (the trail clock).
public ParameterBlock<TrailGlobals2D> trailGlobals;

[shader("vertex")]
public V2F MainVS<T : ITrailSurface>(TrailVertexInput2D input)
{
    V2F output = (V2F)0;
    TrailSample2D s;
    if (!TrySampleTrail2D(trailScene.trailPoints, trailScene.trailParams, trailGlobals.time,
                          input.vertexId, input.instanceId, s))
    {
        output.position = TRAIL_DEAD_CLIP;
        return output;
    }

    // The width envelope: width0 -> width1 eased over the first 3/4 of the
    // point's life, so a fresh head can bulge while the aged tail tapers.
    float halfWidth = lerp(s.trail.envelope.y, s.trail.envelope.z,
                           smoothstep(0.0, 0.75, s.age01));

    // The surface's vertex hook: turbulence wander, width noise - or nothing.
    T surface = T();
    TrailVertexInput surfaceVertex;
    surfaceVertex.centerPosition = s.posDepth;
    surfaceVertex.tangent = float3(s.normal.y, -s.normal.x, 0.0);
    surfaceVertex.side = float3(s.normal, 0.0);
    surfaceVertex.upDirection = float3(0.0, 1.0, 0.0);
    surfaceVertex.uv = float2(s.u, s.across);
    surfaceVertex.distanceFromStart = s.distanceFromStart;
    surfaceVertex.halfWidth = halfWidth;
    surfaceVertex.age = s.age;
    surfaceVertex.age01 = s.age01;
    surfaceVertex.time = trailGlobals.time;
    surfaceVertex.seed = s.trail.misc.x;
    surfaceVertex.userData = s.trail.userData;
    TrailVertexResult ribbon = surface.getVertexAdjustment(surfaceVertex);

    // A shared surface may displace out of the ribbon plane in 3D. Preserve
    // the caller's sorting depth in 2D, where z is not a physical height.
    float2 world = s.posDepth.xy + ribbon.worldOffset.xy
                 + s.normal * (s.across * halfWidth * ribbon.widthScale);
    output.worldPosition = float3(world, s.posDepth.z);
    output.position = mul(camera.viewProjection, float4(output.worldPosition, 1.0));

    // The color gradient over age with the fade envelope's alpha applied: the
    // opacity record x fadeIn (the head ramps in over misc.y of the life) x
    // fadeOut (the tail ramps out over misc.z); a zero fraction disables its
    // ramp. Smooth the endpoints so aging batches do not appear or disappear
    // with the abrupt slope of a linear fade (the original smoke envelope).
    float fadeIn = s.trail.misc.y <= 0.0 ? 1.0 : smoothstep(0.0, s.trail.misc.y, s.age01);
    float fadeOut = s.trail.misc.z <= 0.0 ? 1.0 : smoothstep(0.0, s.trail.misc.z, 1.0 - s.age01);
    float4 color = lerp(s.trail.color0, s.trail.color1, s.age01);
    color.a *= s.trail.envelope.w * fadeIn * fadeOut;

    output.color = color;
    output.uv = float2(s.u, s.across);
    output.age01 = s.age01;
    output.age = s.age;
    output.seed = s.trail.misc.x;
    output.userData = s.trail.userData;
    output.data = ribbon.data;
    return output;
}

[shader("fragment")]
public float4 MainPS<T : ITrailSurface>(V2F input) : SV_TARGET
{
    T surface = T();
    TrailSurfaceInput surfaceInput;
    surfaceInput.uv = input.uv;
    surfaceInput.color = input.color;
    surfaceInput.age = input.age;
    surfaceInput.age01 = input.age01;
    surfaceInput.seed = input.seed;
    surfaceInput.userData = input.userData;
    surfaceInput.time = trailGlobals.time;
    surfaceInput.worldPosition = input.worldPosition;
    surfaceInput.data = input.data;
    return surface.getColor(surfaceInput);
}
