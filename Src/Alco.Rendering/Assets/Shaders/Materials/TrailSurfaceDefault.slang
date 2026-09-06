// file "TrailSurfaceDefault.slang" - module "TrailSurfaceDefault"
//
// The default trail surface: the trail's color gradient (color0 -> color1
// over the point's age, arriving premultiplied with the fade envelope's
// alpha) shaped by a soft across-ribbon edge - the visual of every trail
// whose material names no surface module of its own. The trail systems
// compose it as the material compiler's default surface
// (GpuTrailSystem2D.DefaultSurface).
//
// The edge parameters read zero when the material asset authors none:
// edgeStart 0 spreads the falloff over the whole half width, edgeSharpness 0
// keeps the curve unsharpened - a sensible bare default.

#language slang 2025
module TrailSurfaceDefault;

import AlcoRendering_Core; // [MaterialParams]
import AlcoRendering_TrailSurface; // ITrailSurface, TrailSurfaceInput

public struct DefaultParamsData
{
    public float edgeStart;     // where the across-ribbon falloff begins, in 0..1 of |across|
    public float edgeSharpness; // sharpens the falloff curve beyond the base slope
};

[MaterialParams]
public ParameterBlock<DefaultParamsData> material;

// Fragment-only: the vertex hook keeps its default (ribbon unchanged).
public struct Surface : ITrailSurface
{
    public override float4 getColor(TrailSurfaceInput input)
    {
        float edge = pow(1.0 - smoothstep(material.edgeStart, 1.0, abs(input.uv.y)),
                         1.0 + max(material.edgeSharpness, 0.0));
        float4 color = input.color;
        color.a *= edge;
        // Premultiplied alpha (the trail pass's blend state).
        return float4(color.rgb * color.a, color.a);
    }
}
