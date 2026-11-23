#language slang 2025
module SpriteScenePass;

import Core;
import AlcoRendering_SpriteScene;

[GPURoot]
public struct SpriteScenePassRoot<T : ISpriteSceneSurface>
{
    public SpriteScenePassHeader Pass;
    public T.Params Material;
};

[shader("vertex")]
public SpriteSceneVsOut vsMain<T : ISpriteSceneSurface>(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
{
    SpriteScenePassRoot<T> root = gpu_load_root<SpriteScenePassRoot<T>>();
    uint slot = gpu_load<uint>(root.Pass.VisibleSlots, instanceId);
    SpriteSceneInstance inst = gpu_load<SpriteSceneInstance>(root.Pass.Pool, slot);

    T surface = T();
    SpriteSceneVertexInput vertexInput;
    vertexInput.VertexId = vertexId;
    vertexInput.Time = root.Pass.Time;
    vertexInput.BucketTag = root.Pass.BucketTag;
    vertexInput.Position = inst.Position;
    vertexInput.Rotation = inst.Rotation;
    vertexInput.Scale = inst.Scale;
    vertexInput.Depth = inst.Depth;
    vertexInput.Flags = inst.Flags;
    vertexInput.UserData = inst.UserData;
    vertexInput.CustomData = inst.CustomData;
    vertexInput.MaterialParamId = inst.MaterialParamId;
    vertexInput.MaterialParams = root.Pass.MaterialParams;
    vertexInput.HasMaterialParams = root.Pass.HasMaterialParams;

    SpriteSceneLocalVertex localVert = surface.ExpandLocalVertex(vertexId, vertexInput, root.Material);
    float3 worldPos = surface.TransformToWorld(localVert, vertexInput, root.Material);

    SpriteSceneVsOut output;
    output.Position = mul(float4(worldPos, 1.0), root.Pass.ViewProjection);
    output.Uv = localVert.Uv * inst.UvRect.zw + inst.UvRect.xy;
    output.LocalUv = localVert.Uv;
    output.WorldPos = worldPos;
    output.Color = inst.Color;
    output.CustomData = inst.CustomData;
    output.TextureId = inst.TextureId;
    output.AuxTextureId = inst.AuxTextureId;
    output.Flags = inst.Flags;
    output.MaterialParamId = inst.MaterialParamId;
    output.UserData = inst.UserData;
    return output;
}

[shader("pixel")]
public float4 psMain<T : ISpriteSceneSurface, let alphaTest : bool>(SpriteSceneVsOut input) : SV_Target0
{
    SpriteScenePassRoot<T> root = gpu_load_root<SpriteScenePassRoot<T>>();
    float2 ddxUv = ddx(input.Uv);
    float2 ddyUv = ddy(input.Uv);

    T surface = T();
    SpriteSceneSurfaceInput surf;
    surf.Uv = input.Uv;
    surf.LocalUv = input.LocalUv;
    surf.WorldPos = input.WorldPos;
    surf.ScreenPosition = input.Position.xy;
    surf.DdxUv = ddxUv;
    surf.DdyUv = ddyUv;
    surf.Color = input.Color;
    surf.CustomData = input.CustomData;
    surf.TextureId = input.TextureId;
    surf.AuxTextureId = input.AuxTextureId;
    surf.Flags = input.Flags;
    surf.MaterialParamId = input.MaterialParamId;
    surf.BucketTag = root.Pass.BucketTag;
    surf.UserData = input.UserData;
    surf.Time = root.Pass.Time;
    surf.UserParams = root.Pass.UserParams;
    surf.MaterialParams = root.Pass.MaterialParams;
    surf.HasMaterialParams = root.Pass.HasMaterialParams;

    float4 color = surface.GetColor(surf, root.Material);
    if (alphaTest && color.a < surface.GetAlphaCutoff(root.Material))
    {
        discard;
    }
    return color;
}
