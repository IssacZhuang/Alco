// file "AlcoRendering_Trails3D.slang" - module "AlcoRendering_Trails3D"
//
// GPU-resident trail ribbon machinery paired with GpuTrailSystem3D (Alco.Rendering)
// - the 3D counterpart of AlcoRendering_Trails2D. The CPU appends one point record
// per emission into the trail's ring slice of the shared point buffer; the vertex
// stage handles validity, aging and the camera-facing across-ribbon expansion.
// The CPU scans trail slots for lifecycle, uploads and draw batching.
//
// Geometry contract: same ring-slice + static quad-strip pattern as the 2D
// machinery (see AlcoRendering_Trails2D): one indexed indirect draw per trail
// (firstInstance = trail slot, vertexOffset = sliceOffset * 4), 4 vertices and 6
// indices per segment, logical segment (vid >> 2) - sliceOffset, endpoint
// (vid >> 1) & 1, across-ribbon side vid & 1. Expired segments collapse to
// TRAIL3D_DEAD_CLIP and are clipped before rasterization.
//
// 3D difference: a point bakes the head's travel direction (tangent) instead of
// a ribbon normal, and the across-ribbon axis is derived per vertex to face the
// camera - side = normalize(cross(viewDir, tangent)) with viewDir from the
// trailGlobals camera position - so the ribbon stays a camera-facing strip free
// to wander through 3D space (the 2D machinery's baked-normal expansion cannot).
//
// Look-free module: the trail pass template (GpuTrail3D) declares the parameter
// blocks (the trailPoints/trailParams buffers and the trailGlobals uniform block
// - names are the binding contract) and owns the entry points; the surface
// module (ITrailSurface3D, AlcoRendering_TrailSurface3D) shapes the look. This
// module provides the fetch, validity and aging math plus the standard
// TrailSample3D attributes both build on.

#language slang 2025
module AlcoRendering_Trails3D;

// One trail point record (GPU twin of GpuTrailSystem3D.TrailPoint3D, 32 bytes).
public struct TrailPoint3D
{
    public float3 position;   // world position
    public float emitTime;    // the renderer clock value at emission (seconds)
    public float3 tangent;    // the head's unit travel direction at the point
    public float u;           // arc length in world units
};

// The per-trail parameter record (GPU twin of GpuTrailSystem3D.TrailParams3D, 96 bytes).
public struct TrailParams3D
{
    public float4 envelope;   // x: life (s), y: width0, z: width1, w: opacity
    public float4 color0;     // the ribbon color of a freshly emitted point
    public float4 color1;     // the ribbon color of a fully aged point
    public float4 userData;   // material-defined custom data (TrailEffect3D.UserData)
    public uint4 ring;        // x: sliceOffset, y: capacity, z: cursor, w: written (monotonic)
    public float4 misc;       // x: random seed, y: fadeIn (life fraction), z: fadeOut (life fraction)
};

// The renderer's per-frame globals, bound as the trailGlobals uniform block:
// the trail clock plus the rendering camera's world position (the camera-facing
// expansion needs a view direction per ribbon point).
// (Member order follows the uniform-block layout: the float3 leads so the float
// packs into its tail padding - the C# twin is 16 bytes exactly.)
public struct TrailGlobals3D
{
    public float3 cameraPosition; // the world position of the camera the trails render for
    public float time;           // the renderer clock; point age = time - emitTime
};

// The vertex stage input of a trail pass: no vertex-buffer data, both identities
// come from the Vulkan-semantic builtins.
public struct TrailVertexInput3D
{
    public uint vertexId : SV_VulkanVertexID;
    public uint instanceId : SV_VulkanInstanceID;
};

// The standard attributes of one sampled ribbon vertex.
public struct TrailSample3D
{
    public float3 position;      // world position
    public float3 tangent;       // the head's unit travel direction at the point
    public float3 side;          // the camera-facing across-ribbon axis (unit)
    public float u;              // arc length in world units
    public float distanceFromStart; // arc distance from the first retained ribbon point
    public float across;         // -1..1 across the ribbon
    public float age;            // seconds since the point was emitted
    public float age01;          // age / life, saturated
    public TrailParams3D trail;  // the trail's parameter record (envelope, colors, userData, seed)
};

// The clip-space position dead vertices collapse to: z beyond the far plane, so the
// whole primitive is clipped before rasterization.
public static const float4 TRAIL3D_DEAD_CLIP = float4(0.0, 0.0, 2.0, 1.0);

// Any unit vector perpendicular to v: the fallback across-axis when the travel
// direction points (nearly) at the camera and the cross product degenerates.
public float3 TrailPerpendicular3D(float3 v)
{
    float3 a = abs(v.y) < 0.9 ? float3(0.0, 1.0, 0.0) : float3(0.0, 0.0, 1.0);
    return normalize(cross(a, v));
}

// Resolves the vertex's ribbon sample, or returns false when the vertex belongs to a
// dead segment - the material must then write TRAIL3D_DEAD_CLIP as the vertex position.
public bool TrySampleTrail3D(
    RWStructuredBuffer<TrailPoint3D> trailPoints,
    RWStructuredBuffer<TrailParams3D> trailParams,
    float time,
    float3 cameraPosition,
    uint vertexId,
    uint trailSlot,
    out TrailSample3D sample)
{
    sample = (TrailSample3D)0;
    TrailParams3D trail = trailParams[trailSlot];
    uint sliceOffset = trail.ring.x;
    uint capacity = trail.ring.y;
    uint cursor = trail.ring.z;
    uint written = trail.ring.w;

    uint segment = (vertexId >> 2) - sliceOffset;
    uint window = min(written, capacity);
    if (capacity == 0 || segment + 1 >= window) return false;

    // Chronological traversal includes capacity-1 -> 0 when it is live and
    // excludes newest -> oldest. Slice capacities are powers of two.
    uint oldest = written >= capacity ? cursor : 0;
    uint localLeft = (oldest + segment) & (capacity - 1);
    uint localRight = (localLeft + 1) & (capacity - 1);

    TrailPoint3D pointA = trailPoints[sliceOffset + localLeft];
    TrailPoint3D pointB = trailPoints[sliceOffset + localRight];
    float life = max(trail.envelope.x, 1e-4);
    float ageA = time - pointA.emitTime;
    float ageB = time - pointB.emitTime;
    // An expired endpoint kills the segment; points expire oldest-first, so the
    // ribbon erodes smoothly from the tail.
    if (ageA > life || ageB > life) return false;

    bool right = ((vertexId >> 1) & 1) != 0;
    TrailPoint3D point = right ? pointB : pointA;
    float age = right ? ageB : ageA;
    sample.position = point.position;
    sample.tangent = point.tangent;

    // The camera-facing across-ribbon axis: perpendicular to both the travel
    // direction and the view direction of this point. A head flying straight at
    // the camera degenerates to the perpendicular fallback (the ribbon edge-on
    // case, hidden by its vanishing width anyway).
    float3 viewDir = point.position - cameraPosition;
    float3 side = cross(viewDir, point.tangent);
    float sideLength = length(side);
    sample.side = sideLength > 1e-4 ? side / sideLength : TrailPerpendicular3D(point.tangent);

    sample.u = point.u;
    // Emission starts one spacing interval beyond the anchor; use the actual
    // first retained point so the ribbon's starting edge reaches zero alpha.
    sample.distanceFromStart = point.u - trailPoints[sliceOffset + oldest].u;
    sample.across = float(vertexId & 1) * 2.0 - 1.0;
    sample.age = age;
    sample.age01 = saturate(age / life);
    sample.trail = trail;
    return true;
}
