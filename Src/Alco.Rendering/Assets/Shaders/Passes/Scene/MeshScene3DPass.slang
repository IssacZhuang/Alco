#language slang 2025
module MeshScene3DPass;

import Core;
import AlcoRendering_MeshScene3D;

[GPURoot]
public struct MeshScene3DPassRoot<T : IMeshScene3DSurface>
{
    public MeshScene3DPassHeader Pass;
    public T.Params Material;
};

[shader("vertex")]
public MeshScene3DVsOut vsMain<T : IMeshScene3DSurface>(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
{
    MeshScene3DPassRoot<T> root = gpu_load_root<MeshScene3DPassRoot<T>>();
    uint slot = meshScene3DLoadSlot(root.Pass.VisibleSlots, instanceId);
    MeshScene3DInstance inst = meshScene3DLoadInstance(root.Pass.Pool, slot);
    inst.World0.w -= root.Pass.OriginOffset.x;
    inst.World1.w -= root.Pass.OriginOffset.y;
    inst.World2.w -= root.Pass.OriginOffset.z;
    MeshScene3DVertex vert = meshScene3DLoadVertex(root.Pass.Vertices, vertexId);

    MeshScene3DVertexInput vertexInput;
    vertexInput.VertexId = vertexId;
    vertexInput.InstanceId = instanceId;
    vertexInput.Slot = slot;
    vertexInput.Vertex = vert;
    vertexInput.Instance = inst;
    vertexInput.Time = root.Pass.Time;
    vertexInput.BucketTag = root.Pass.BucketTag;
    vertexInput.PassFlags = root.Pass.PassFlags;
    vertexInput.UserParams = root.Pass.UserParams;
    vertexInput.MaterialParams = root.Pass.MaterialParams;
    vertexInput.HasMaterialParams = root.Pass.HasMaterialParams;

    T surface = T();
    MeshScene3DTransformedVertex transformed = surface.TransformVertex(vertexInput, root.Material);

    MeshScene3DVsOut output;
    output.Position = mul(float4(transformed.WorldPos, 1.0), root.Pass.ViewProjection);
    output.WorldPos = transformed.WorldPos;
    output.WorldNormal = transformed.WorldNormal;
    output.Uv = transformed.Uv;
    output.Color = inst.Color;
    output.CustomData0 = inst.CustomData0;
    output.CustomData1 = inst.CustomData1;
    output.Slot = slot;
    output.TextureId = inst.TextureId;
    output.AuxTextureId = inst.AuxTextureId;
    output.Flags = inst.Flags;
    output.MaterialParamId = inst.MaterialParamId;
    output.UserData0 = inst.UserData0;
    output.UserData1 = inst.UserData1;
    return output;
}

[shader("pixel")]
public float4 psMain<T : IMeshScene3DSurface, let alphaTest : bool>(MeshScene3DVsOut input) : SV_Target0
{
    MeshScene3DPassRoot<T> root = gpu_load_root<MeshScene3DPassRoot<T>>();
    float2 ddxUv = ddx(input.Uv);
    float2 ddyUv = ddy(input.Uv);

    MeshScene3DSurfaceInput surf;
    surf.WorldPos = input.WorldPos;
    surf.WorldNormal = input.WorldNormal;
    surf.Uv = input.Uv;
    surf.ScreenPosition = input.Position.xy;
    surf.DdxUv = ddxUv;
    surf.DdyUv = ddyUv;
    surf.Color = input.Color;
    surf.CustomData0 = input.CustomData0;
    surf.CustomData1 = input.CustomData1;
    surf.Slot = input.Slot;
    surf.TextureId = input.TextureId;
    surf.AuxTextureId = input.AuxTextureId;
    surf.MaterialParamId = input.MaterialParamId;
    surf.Flags = input.Flags;
    surf.UserData0 = input.UserData0;
    surf.UserData1 = input.UserData1;
    surf.BucketTag = root.Pass.BucketTag;
    surf.PassFlags = root.Pass.PassFlags;
    surf.Time = root.Pass.Time;
    surf.UserParams = root.Pass.UserParams;
    surf.MaterialParams = root.Pass.MaterialParams;
    surf.HasMaterialParams = root.Pass.HasMaterialParams;

    T surface = T();
    float4 color = surface.GetColor(surf, root.Material);
    if (alphaTest && color.a < surface.GetAlphaCutoff(root.Material))
    {
        discard;
    }
    return color;
}
