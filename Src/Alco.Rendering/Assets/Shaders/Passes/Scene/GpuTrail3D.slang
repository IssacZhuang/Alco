// file "GpuTrail3D.slang" - module "GpuTrail3D"
//
// The built-in 3D trail render pass TEMPLATE: draws the GPU-resident ribbon
// machinery of GpuTrailSystem3D (AlcoRendering_Trails3D - shared point ring
// buffers, multi-draw-indirect over a static quad-strip index pattern) as a
// camera-facing strip: the across-ribbon axis is derived per vertex from the
// point's baked travel tangent and the trailGlobals camera position, so trails
// wander freely through 3D space (the 2D pass bakes the ribbon normal at
// emission instead). Both entries are generic over the trail's surface
// (ITrailSurface, AlcoRendering_TrailSurface): the material compiler
// composes this module with the trail material's surface module, so the visual
// comes from the .amat - the built-in default surface (TrailSurfaceDefault)
// shades the color gradient with a soft across-ribbon edge and leaves the
// vertex stage untouched (the getVertexAdjustment hook's default).
//
// The pass owns the resource contract the trail system binds: the "camera"
// block (auto-bound view-projection), the "trailScene" block (the trail
// point/parameter ring buffers) and the "trailGlobals" block (the renderer
// clock plus the camera's world position). The surface module owns the material
// resources (textures and [MaterialParams] values). The template computes the
// ribbon geometry - point fetch, validity, aging, the camera-facing expansion,
// the width0 -> width1 envelope, the fade in/out alpha envelope and the
// projection - so a surface shapes the look, not the machinery.
//
// Depth is tested but not written (the material's depth stencil state); the
// output is a single premultiplied-alpha slot.

#language slang 2025
module GpuTrail3D;

import AlcoRendering_Trails3D;
import AlcoRendering_TrailSurface;

public struct V2F
{
    public float4 position : SV_POSITION;
    public float2 uv : TEXCOORD0;      // x: arc length, y: -1..1 across the ribbon
    public float4 color : COLOR0;      // the color gradient, envelope alpha applied
    public float age01 : TEXCOORD1;    // the normalized point age in [0, 1]
    public float age : TEXCOORD2;      // seconds since the point was emitted
    public float seed : TEXCOORD3;     // the trail's random seed
    public float4 userData : TEXCOORD4; // the trail's custom data channel
    public float4 data : TEXCOORD5;    // the surface hook's private VS -> FS channel
    public float3 worldPosition : TEXCOORD6; // the ribbon vertex's world position (post vertex hook)
};

public struct CameraParams
{
    public float4x4 viewProjection;
};

public ParameterBlock<CameraParams> camera;

// The GpuTrailSystem3D buffers (the member names are the binding contract).
public struct TrailSceneParams
{
    public RWStructuredBuffer<TrailPoint3D> trailPoints;
    public RWStructuredBuffer<TrailParams3D> trailParams;
};

public ParameterBlock<TrailSceneParams> trailScene;

// The renderer's per-frame globals (the trail clock and the camera position).
public ParameterBlock<TrailGlobals3D> trailGlobals;

[shader("vertex")]
public V2F MainVS<T : ITrailSurface>(TrailVertexInput3D input)
{
    V2F output = (V2F)0;
    TrailSample3D s;
    if (!TrySampleTrail3D(trailScene.trailPoints, trailScene.trailParams, trailGlobals.time,
                          trailGlobals.cameraPosition, input.vertexId, input.instanceId, s))
    {
        output.position = TRAIL3D_DEAD_CLIP;
        return output;
    }

    // The width envelope: width0 -> width1 eased over the first 3/4 of the
    // point's life, so a fresh head can bulge while the aged tail tapers.
    float halfWidth = lerp(s.trail.envelope.y, s.trail.envelope.z,
                           smoothstep(0.0, 0.75, s.age01));

    // The surface's vertex hook: turbulence wander, buoyant rise, width noise -
    // or nothing.
    T surface = T();
    TrailVertexInput surfaceVertex;
    surfaceVertex.centerPosition = s.position;
    surfaceVertex.tangent = s.tangent;
    surfaceVertex.side = s.side;
    surfaceVertex.upDirection = float3(0.0, 0.0, 1.0);
    surfaceVertex.uv = float2(s.u, s.across);
    surfaceVertex.distanceFromStart = s.distanceFromStart;
    surfaceVertex.halfWidth = halfWidth;
    surfaceVertex.age = s.age;
    surfaceVertex.age01 = s.age01;
    surfaceVertex.time = trailGlobals.time;
    surfaceVertex.seed = s.trail.misc.x;
    surfaceVertex.userData = s.trail.userData;
    TrailVertexResult ribbon = surface.getVertexAdjustment(surfaceVertex);

    // The camera-facing strip: the across-ribbon offset rides the side axis the
    // sampler derived from the view direction and the travel tangent.
    float3 world = s.position + ribbon.worldOffset
                 + s.side * (s.across * halfWidth * ribbon.widthScale);
    output.position = mul(camera.viewProjection, float4(world, 1.0));
    output.worldPosition = world;

    // The color gradient over age with the fade envelope's alpha applied: the
    // opacity record x fadeIn (the head ramps in over misc.y of the life) x
    // fadeOut (the tail ramps out over misc.z); a zero fraction disables its
    // ramp. Ease the endpoints, matching the 2D trail envelope, so fading smoke
    // does not expose abrupt brightness steps between aging batches.
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
