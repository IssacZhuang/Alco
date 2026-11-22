// file "GpuTrail2D.slang" - module "GpuTrail2D"
//
// Built-in 2D trail render pass TEMPLATE: draws active ribbon segments via
// vertex-pulling instancing, composed with ITrailSurface modules.

#language slang 2025
module GpuTrail2D;

import Core;
import AlcoRendering_Trails2D;
import AlcoRendering_TrailSurface;

public struct TrailPassData2D
{
    public float4x4 viewProjection;
    public GPUBufferView trailPoints;
    public GPUBufferView trailParams;
    public GPUBufferView segments;
    public float time;
};

[GPURoot]
public struct TrailPassRoot2D<T : ITrailSurface>
{
    public TrailPassData2D Pass;
    public T.Params Material;
};

public struct V2F
{
    public float4 position : SV_POSITION;
    public float2 uv : TEXCOORD0;
    public float4 color : COLOR0;
    public float age01 : TEXCOORD1;
    public float age : TEXCOORD2;
    public float seed : TEXCOORD3;
    public float4 userData : TEXCOORD4;
    public float4 data : TEXCOORD5;
    public float3 worldPosition : TEXCOORD6;
};

[shader("vertex")]
public V2F MainVS<T : ITrailSurface>(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
{
    TrailPassRoot2D<T> root = gpu_load_root<TrailPassRoot2D<T>>();
    TrailSegment seg = gpu_load<TrailSegment>(root.Pass.segments, instanceId);

    V2F output = (V2F)0;
    TrailSample2D s;
    if (!TrySampleTrail2D(root.Pass.trailPoints, root.Pass.trailParams, root.Pass.time,
                          seg.trailSlot, seg.segment, vertexId, s))
    {
        output.position = TRAIL_DEAD_CLIP;
        return output;
    }

    float halfWidth = lerp(s.trail.envelope.y, s.trail.envelope.z,
                           smoothstep(0.0, 0.75, s.age01));

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
    surfaceVertex.time = root.Pass.time;
    surfaceVertex.seed = s.trail.misc.x;
    surfaceVertex.userData = s.trail.userData;
    TrailVertexResult ribbon = surface.getVertexAdjustment(surfaceVertex, root.Material);

    float2 world = s.posDepth.xy + ribbon.worldOffset.xy
                 + s.normal * (s.across * halfWidth * ribbon.widthScale);
    output.worldPosition = float3(world, s.posDepth.z);
    output.position = mul(float4(output.worldPosition, 1.0), root.Pass.viewProjection);

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
    TrailPassRoot2D<T> root = gpu_load_root<TrailPassRoot2D<T>>();
    T surface = T();
    TrailSurfaceInput surfaceInput;
    surfaceInput.uv = input.uv;
    surfaceInput.color = input.color;
    surfaceInput.age = input.age;
    surfaceInput.age01 = input.age01;
    surfaceInput.seed = input.seed;
    surfaceInput.userData = input.userData;
    surfaceInput.time = root.Pass.time;
    surfaceInput.worldPosition = input.worldPosition;
    surfaceInput.data = input.data;
    return surface.getColor(surfaceInput, root.Material);
}
