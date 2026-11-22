// file "AlcoRendering_TrailSurface.slang" - module "AlcoRendering_TrailSurface"
//
// The surface contract between trail material modules and GpuTrail2D/3D pass templates.

#language slang 2025
module AlcoRendering_TrailSurface;

import Core;

/// Per-fragment inputs shaded by a trail surface.
public struct TrailSurfaceInput
{
    public float2 uv;
    public float4 color;
    public float age;
    public float age01;
    public float seed;
    public float4 userData;
    public float time;
    public float3 worldPosition;
    public float4 data;
};

/// Dimension-independent per-vertex inputs passed to the trail ribbon adjustment hook.
public struct TrailVertexInput
{
    public float3 centerPosition;
    public float3 tangent;
    public float3 side;
    public float3 upDirection;
    public float2 uv;
    public float distanceFromStart;
    public float halfWidth;
    public float age;
    public float age01;
    public float time;
    public float seed;
    public float4 userData;
};

/// Per-vertex adjustment returned by ITrailSurface.getVertexAdjustment.
public struct TrailVertexResult
{
    public float3 worldOffset;
    public float widthScale;
    public float4 data;
};

/// Empty parameter block placeholder for parameterless trail surfaces.
public struct EmptyTrailParams
{
    public uint _pad;
};

/// Trail surface contract shared by 2D and 3D trail passes.
public interface ITrailSurface
{
    associatedtype Params;

    float4 getColor(TrailSurfaceInput input, Params p)
    {
        return float4(input.color.rgb * input.color.a, input.color.a);
    }

    TrailVertexResult getVertexAdjustment(TrailVertexInput input, Params p)
    {
        TrailVertexResult result;
        result.worldOffset = float3(0.0, 0.0, 0.0);
        result.widthScale = 1.0;
        result.data = float4(0.0, 0.0, 0.0, 0.0);
        return result;
    }
}
