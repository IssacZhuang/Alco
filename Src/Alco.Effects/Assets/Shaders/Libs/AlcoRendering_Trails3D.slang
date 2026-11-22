// file "AlcoRendering_Trails3D.slang" - module "AlcoRendering_Trails3D"
//
// GPU-resident 3D trail ribbon machinery paired with GpuTrailSystem3D (Alco.Effects).
// Segment-instanced camera-facing ribbon strips.

#language slang 2025
module AlcoRendering_Trails3D;

import Core;
import AlcoRendering_Trails2D;

// One trail point record (GPU twin of TrailPoint3D, 32 bytes).
public struct TrailPoint3D
{
    public float3 position;   // world position
    public float emitTime;    // the renderer clock value at emission (seconds)
    public float3 tangent;    // the head's unit travel direction at the point
    public float u;           // arc length in world units
};

// The per-trail parameter record (GPU twin of TrailParams3D, 96 bytes).
public struct TrailParams3D
{
    public float4 envelope;   // x: life (s), y: width0, z: width1, w: opacity
    public float4 color0;     // the ribbon color of a freshly emitted point
    public float4 color1;     // the ribbon color of a fully aged point
    public float4 userData;   // material-defined custom data (TrailEffect3D.UserData)
    public uint4 ring;        // x: sliceOffset, y: capacity, z: cursor, w: written (monotonic)
    public float4 misc;       // x: random seed, y: fadeIn (life fraction), z: fadeOut (life fraction)
};

// The standard attributes of one sampled ribbon vertex.
public struct TrailSample3D
{
    public float3 position;
    public float3 tangent;
    public float3 side;
    public float u;
    public float distanceFromStart;
    public float across;
    public float age;
    public float age01;
    public TrailParams3D trail;
};

public static const float4 TRAIL3D_DEAD_CLIP = float4(0.0, 0.0, 2.0, 1.0);

public float3 TrailPerpendicular3D(float3 v)
{
    float3 a = abs(v.y) < 0.9 ? float3(0.0, 1.0, 0.0) : float3(0.0, 0.0, 1.0);
    return normalize(cross(a, v));
}

public bool TrySampleTrail3D(
    GPUBufferView trailPoints,
    GPUBufferView trailParams,
    float time,
    float3 cameraPosition,
    uint trailSlot,
    uint segment,
    uint vertexId,
    out TrailSample3D sample)
{
    sample = (TrailSample3D)0;
    TrailParams3D trail = gpu_load<TrailParams3D>(trailParams, trailSlot);
    uint sliceOffset = trail.ring.x;
    uint capacity = trail.ring.y;
    uint cursor = trail.ring.z;
    uint written = trail.ring.w;

    uint window = min(written, capacity);
    if (capacity == 0u || segment + 1u >= window) return false;

    uint oldest = written >= capacity ? cursor : 0u;
    uint localLeft = (oldest + segment) & (capacity - 1u);
    uint localRight = (localLeft + 1u) & (capacity - 1u);

    TrailPoint3D pointA = gpu_load<TrailPoint3D>(trailPoints, sliceOffset + localLeft);
    TrailPoint3D pointB = gpu_load<TrailPoint3D>(trailPoints, sliceOffset + localRight);
    float life = max(trail.envelope.x, 1e-4);
    float ageA = time - pointA.emitTime;
    float ageB = time - pointB.emitTime;
    if (ageA > life || ageB > life) return false;

    uint corner = trailQuadCorner(vertexId);
    bool right = ((corner >> 1) & 1u) != 0u;
    TrailPoint3D point = right ? pointB : pointA;
    float age = right ? ageB : ageA;
    sample.position = point.position;
    sample.tangent = point.tangent;

    float3 viewDir = point.position - cameraPosition;
    float3 side = cross(viewDir, point.tangent);
    float sideLength = length(side);
    sample.side = sideLength > 1e-4 ? side / sideLength : TrailPerpendicular3D(point.tangent);

    sample.u = point.u;
    sample.distanceFromStart = point.u - gpu_load<TrailPoint3D>(trailPoints, sliceOffset + oldest).u;
    sample.across = float(corner & 1u) * 2.0 - 1.0;
    sample.age = age;
    sample.age01 = saturate(age / life);
    sample.trail = trail;
    return true;
}
