#language slang 2025
module Sprite2DPass;

import Core;
import AlcoRendering_Sprite2D;

[GPURoot]
public struct Sprite2DPassRoot<T : ISprite2DSurface>
{
    public Sprite2DPassHeader Pass;
    public T.Params Material;
};

[shader("vertex")]
public Sprite2DVsOut vsMain<T : ISprite2DSurface>(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
{
    Sprite2DPassRoot<T> root = gpu_load_root<Sprite2DPassRoot<T>>();
    Sprite2DQuad quad = gpu_load<Sprite2DQuad>(root.Pass.Quads, instanceId);
    return sprite2DExpandQuadVertex(quad, root.Pass.ViewProjection, vertexId);
}

[shader("pixel")]
public float4 psMain<T : ISprite2DSurface>(Sprite2DVsOut input) : SV_Target0
{
    Sprite2DPassRoot<T> root = gpu_load_root<Sprite2DPassRoot<T>>();
    Sprite2DSurfaceInput surf;
    surf.Uv = input.Uv;
    surf.LocalUv = input.LocalUv;
    surf.WorldPos = input.WorldPos;
    surf.Color = input.Color;
    surf.BaseColor = sprite2DShade(input);
    surf.TextureIndex = input.TextureIndex;
    surf.Flags = input.Flags;
    surf.Time = root.Pass.Time;

    T surface = T();
    return surface.Shade(surf, root.Material);
}
