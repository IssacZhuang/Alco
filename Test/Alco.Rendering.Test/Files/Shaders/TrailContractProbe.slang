// file "TrailContractProbe.slang" - module "TrailContractProbe"
// Exposes the final world position after tangent/side/up offsets and width expansion.

#language slang 2025
module TrailContractProbe;

import AlcoRendering_TrailSurface;

public struct Surface : ITrailSurface
{
    public override TrailVertexResult getVertexAdjustment(TrailVertexInput input)
    {
        TrailVertexResult result;
        result.worldOffset = input.tangent * 0.125 + input.side * 0.125
            + input.upDirection * input.userData.x + float3(0.0, 0.0, input.userData.y);
        result.widthScale = 2.0;
        result.data = float4(0.0, 0.0, 0.0, 0.0);
        return result;
    }

    public override float4 getColor(TrailSurfaceInput input)
    {
        return float4(input.worldPosition, 1.0);
    }
}
