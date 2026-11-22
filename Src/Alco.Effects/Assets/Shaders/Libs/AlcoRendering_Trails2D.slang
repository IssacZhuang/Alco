// file "AlcoRendering_Trails2D.slang" - module "AlcoRendering_Trails2D"
//
// GPU-resident 2D trail ribbon machinery paired with GpuTrailSystem2D (Alco.Effects).
// Segment-instanced rendering: each instanceId indexes a TrailSegment(trailSlot, segment)
// record; vertexId 0..5 expands the 2-triangle ribbon quad for that segment.

#language slang 2025
module AlcoRendering_Trails2D;

import Core;

// One trail point record (GPU twin of TrailPoint2D, 32 bytes).
public struct TrailPoint2D
{
    public float3 posDepth;   // xy: world position, z: render depth
    public float emitTime;    // the renderer clock value at emission (seconds)
    public float2 normal;     // the ribbon normal at the point
    public float u;           // arc length in world units
    public float _pad;
};

// The per-trail parameter record (GPU twin of TrailParams2D, 96 bytes).
public struct TrailParams2D
{
    public float4 envelope;   // x: life (s), y: width0, z: width1, w: opacity
    public float4 color0;     // the ribbon color of a freshly emitted point
    public float4 color1;     // the ribbon color of a fully aged point
    public float4 userData;   // material-defined custom data (TrailEffect2D.UserData)
    public uint4 ring;        // x: sliceOffset, y: capacity, z: cursor, w: written (monotonic)
    public float4 misc;       // x: random seed, y: fadeIn (life fraction), z: fadeOut (life fraction)
};

// Per-instance segment dispatch record (GPU twin of TrailSegment, 8 bytes).
public struct TrailSegment
{
    public uint trailSlot;
    public uint segment;
};

// The standard attributes of one sampled ribbon vertex.
public struct TrailSample2D
{
    public float3 posDepth;
    public float2 normal;
    public float u;
    public float distanceFromStart;
    public float across;
    public float age;
    public float age01;
    public TrailParams2D trail;
};

public static const float4 TRAIL_DEAD_CLIP = float4(0.0, 0.0, 2.0, 1.0);

public uint trailQuadCorner(uint vertexId)
{
    switch (vertexId)
    {
    case 0u: return 0u;
    case 1u: return 1u;
    case 2u: return 2u;
    case 3u: return 2u;
    case 4u: return 1u;
    default: return 3u;
    }
}

public bool TrySampleTrail2D(
    GPUBufferView trailPoints,
    GPUBufferView trailParams,
    float time,
    uint trailSlot,
    uint segment,
    uint vertexId,
    out TrailSample2D sample)
{
    sample = (TrailSample2D)0;
    TrailParams2D trail = gpu_load<TrailParams2D>(trailParams, trailSlot);
    uint sliceOffset = trail.ring.x;
    uint capacity = trail.ring.y;
    uint cursor = trail.ring.z;
    uint written = trail.ring.w;

    uint window = min(written, capacity);
    if (capacity == 0u || segment + 1u >= window) return false;

    uint oldest = written >= capacity ? cursor : 0u;
    uint localLeft = (oldest + segment) & (capacity - 1u);
    uint localRight = (localLeft + 1u) & (capacity - 1u);

    TrailPoint2D pointA = gpu_load<TrailPoint2D>(trailPoints, sliceOffset + localLeft);
    TrailPoint2D pointB = gpu_load<TrailPoint2D>(trailPoints, sliceOffset + localRight);
    float life = max(trail.envelope.x, 1e-4);
    float ageA = time - pointA.emitTime;
    float ageB = time - pointB.emitTime;
    if (ageA > life || ageB > life) return false;

    uint corner = trailQuadCorner(vertexId);
    bool right = ((corner >> 1) & 1u) != 0u;
    TrailPoint2D point = right ? pointB : pointA;
    float age = right ? ageB : ageA;
    sample.posDepth = point.posDepth;
    sample.normal = point.normal;
    sample.u = point.u;
    sample.distanceFromStart = point.u - gpu_load<TrailPoint2D>(trailPoints, sliceOffset + oldest).u;
    sample.across = float(corner & 1u) * 2.0 - 1.0;
    sample.age = age;
    sample.age01 = saturate(age / life);
    sample.trail = trail;
    return true;
}
