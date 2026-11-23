#language slang 2025
module Sprite2DDefault;

import Core;
import AlcoRendering_Sprite2D;

[GPURoot]
public struct Sprite2DDefaultRoot
{
    public Sprite2DPassHeader Pass;
};

[shader("vertex")]
public Sprite2DVsOut vsMain(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
{
    Sprite2DDefaultRoot root = gpu_load_root<Sprite2DDefaultRoot>();
    Sprite2DQuad quad = gpu_load<Sprite2DQuad>(root.Pass.Quads, instanceId);
    return sprite2DExpandQuadVertex(quad, root.Pass.ViewProjection, vertexId);
}

[shader("pixel")]
public float4 psMain(Sprite2DVsOut input) : SV_Target0
{
    return sprite2DShade(input);
}
