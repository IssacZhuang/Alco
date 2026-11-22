// file "TrailSurfaceDefault.slang" - module "TrailSurfaceDefault"
//
// The default 2D/3D trail surface: shades the ribbon gradient with a soft
// across-ribbon edge (premultiplied alpha).

#language slang 2025
module TrailSurfaceDefault;

import Core;
import AlcoRendering_TrailSurface;

public struct DefaultParamsData
{
    public float edgeStart;
    public float edgeSharpness;
};

public struct Surface : ITrailSurface
{
    public typedef DefaultParamsData Params;

    public override float4 getColor(TrailSurfaceInput input, Params p)
    {
        float edge = pow(1.0 - smoothstep(p.edgeStart, 1.0, abs(input.uv.y)),
                         1.0 + max(p.edgeSharpness, 0.0));
        float4 color = input.color;
        color.a *= edge;
        return float4(color.rgb * color.a, color.a);
    }
}
