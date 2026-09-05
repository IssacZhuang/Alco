// file "AlcoRendering_Trails2D.slang" - module "AlcoRendering_Trails2D"
//
// GPU-resident trail ribbon machinery paired with GpuTrailSystem2D (Alco.Rendering).
// The CPU appends one point record per emission into the trail's ring slice of the
// shared point buffer; the vertex stage handles validity, aging and across-ribbon
// expansion. The CPU scans trail slots for lifecycle, uploads and draw batching.
//
// Geometry contract: every trail owns a ring slice of the shared point buffer; the
// renderer multi-draws one indexed draw per trail (firstInstance = trail slot,
// vertexOffset = sliceOffset * 4) over a static quad-strip index pattern of 4
// vertices and 6 indices per segment. The logical segment is (vid >> 2) - sliceOffset,
// its endpoint is (vid >> 1) & 1 and its across-ribbon side is vid & 1 (VertexIndex
// includes the draw's vertexOffset on Vulkan). Logical segments walk from the
// oldest written point, wrapping both endpoints within the slice. Expired segments
// collapse to TRAIL_DEAD_CLIP and are clipped before rasterization.
//
// Look-free module: the trail pass template (GpuTrail2D) declares the parameter
// blocks (the trailPoints/trailParams buffers and the trailGlobals uniform block
// — names are the binding contract) and owns the entry points; the surface
// module (ITrailSurface, AlcoRendering_TrailSurface) shapes the look. This
// module provides the fetch, validity and aging math plus the standard
// TrailSample2D attributes both build on.

#language slang 2025
module AlcoRendering_Trails2D;

// One trail point record (GPU twin of GpuTrailSystem2D.TrailPoint2D, 32 bytes).
public struct TrailPoint2D
{
    public float3 posDepth;   // xy: world position, z: render depth
    public float emitTime;    // the renderer clock value at emission (seconds)
    public float2 normal;     // the ribbon normal at the point
    public float u;           // arc length in world units
    public float _pad;
};

// The per-trail parameter record (GPU twin of GpuTrailSystem2D.TrailParams2D, 96 bytes).
public struct TrailParams2D
{
    public float4 envelope;   // x: life (s), y: width0, z: width1, w: opacity
    public float4 color0;     // the ribbon color of a freshly emitted point
    public float4 color1;     // the ribbon color of a fully aged point
    public float4 userData;   // material-defined custom data (TrailEffect2D.UserData)
    public uint4 ring;        // x: sliceOffset, y: capacity, z: cursor, w: written (monotonic)
    public float4 misc;       // x: random seed, y: fadeIn (life fraction), z: fadeOut (life fraction)
};

// The renderer's per-frame globals, bound as the trailGlobals uniform block.
public struct TrailGlobals2D
{
    public float time;        // the renderer clock; point age = time - emitTime
};

// The vertex stage input of a trail pass: no vertex-buffer data, both identities
// come from the Vulkan-semantic builtins.
public struct TrailVertexInput2D
{
    public uint vertexId : SV_VulkanVertexID;
    public uint instanceId : SV_VulkanInstanceID;
};

// The standard attributes of one sampled ribbon vertex.
public struct TrailSample2D
{
    public float3 posDepth;      // xy: world position, z: render depth
    public float2 normal;        // the ribbon normal at the point
    public float u;              // arc length in world units
    public float across;         // -1..1 across the ribbon
    public float age;            // seconds since the point was emitted
    public float age01;          // age / life, saturated
    public TrailParams2D trail;  // the trail's parameter record (envelope, colors, userData, seed)
};

// The clip-space position dead vertices collapse to: z beyond the far plane, so the
// whole primitive is clipped before rasterization.
public static const float4 TRAIL_DEAD_CLIP = float4(0.0, 0.0, 2.0, 1.0);

// Resolves the vertex's ribbon sample, or returns false when the vertex belongs to a
// dead segment — the material must then write TRAIL_DEAD_CLIP as the vertex position.
public bool TrySampleTrail2D(
    RWStructuredBuffer<TrailPoint2D> trailPoints,
    RWStructuredBuffer<TrailParams2D> trailParams,
    float time,
    uint vertexId,
    uint trailSlot,
    out TrailSample2D sample)
{
    sample = (TrailSample2D)0;
    TrailParams2D trail = trailParams[trailSlot];
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

    TrailPoint2D pointA = trailPoints[sliceOffset + localLeft];
    TrailPoint2D pointB = trailPoints[sliceOffset + localRight];
    float life = max(trail.envelope.x, 1e-4);
    float ageA = time - pointA.emitTime;
    float ageB = time - pointB.emitTime;
    // An expired endpoint kills the segment; points expire oldest-first, so the
    // ribbon erodes smoothly from the tail.
    if (ageA > life || ageB > life) return false;

    bool right = ((vertexId >> 1) & 1) != 0;
    TrailPoint2D point = right ? pointB : pointA;
    float age = right ? ageB : ageA;
    sample.posDepth = point.posDepth;
    sample.normal = point.normal;
    sample.u = point.u;
    sample.across = float(vertexId & 1) * 2.0 - 1.0;
    sample.age = age;
    sample.age01 = saturate(age / life);
    sample.trail = trail;
    return true;
}
