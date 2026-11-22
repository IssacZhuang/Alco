// file "GpuTrail3D.slang" - module "GpuTrail3D"
//
// Built-in 3D trail render pass TEMPLATE: draws active camera-facing ribbon
// segments via vertex-pulling instancing, composed with ITrailSurface modules.

#language slang 2025
module GpuTrail3D;

import Core;
import AlcoRendering_Trails2D;
import AlcoRendering_Trails3D;
import AlcoRendering_TrailSurface;

public struct TrailPassData3D
{
    public float4x4 viewProjection;
    public GPUBufferView trailPoints;
    public GPUBufferView trailParams;
    public GPUBufferView segments;
    public float3 cameraPosition;
    public float time;
};

[GPURoot]
public struct TrailPassRoot3D<T : ITrailSurface>
{
    public TrailPassData3D Pass;
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
    TrailPassRoot3D<T> root = gpu_load_root<TrailPassRoot3D<T>>();
    TrailSegment seg = gpu_load<TrailSegment>(root.Pass.segments, instanceId);

    V2F output = (V2F)0;
    TrailSample3D s;
    if (!TrySampleTrail3D(root.Pass.trailPoints, root.Pass.trailParams, root.Pass.time,
                          root.Pass.cameraPosition, seg.trailSlot, seg.segment, vertexId, s))
    {
        output.position = TRAIL3D_DEAD_CLIP;
        return output;
    }

    float halfWidth = lerp(s.trail.envelope.y, s.trail.envelope.z,
                           smoothstep(0.0, 0.75, s.age01));

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
    surfaceVertex.time = root.Pass.time;
    surfaceVertex.seed = s.trail.misc.x;
    surfaceVertex.userData = s.trail.userData;
    TrailVertexResult ribbon = surface.getVertexAdjustment(surfaceVertex, root.Material);

    float3 world = s.position + ribbon.worldOffset
                 + s.side * (s.across * halfWidth * ribbon.widthScale);
    output.position = mul(float4(world, 1.0), root.Pass.viewProjection);
    output.worldPosition = world;

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
    TrailPassRoot3D<T> root = gpu_load_root<TrailPassRoot3D<T>>();
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
