#language slang 2025
module World3DShadowDepth;

import Core;
import World3DSceneData;
import World3DSurface;

[GPURoot]
public struct World3DShadowDrawRoot
{
    public GPUBufferView Directory;
    public GPUBufferView Views;
    public GPUBufferView DrawRefs;
    public GPUBufferView PayloadPage;
    public uint FirstDraw;
    public uint ViewIndex;
};

struct World3DDrawRef
{
    uint InstanceSlot;
    uint Lod;
};

public struct ShadowVaryings
{
    public float4 Position : SV_Position;
    public float3 EyeRelativePosition : TEXCOORD0;
    public float2 UV : TEXCOORD1;
    public nointerpolation uint MaterialSlot : TEXCOORD2;
    public nointerpolation float Fade : TEXCOORD3;
};

float3 normalizeOr(float3 direction, float3 fallback)
{
    float lengthSquared = dot(direction, direction);
    return lengthSquared > 0.0 && isfinite(lengthSquared) ? direction * rsqrt(lengthSquared) : fallback;
}

[shader("vertex")]
#ifdef ALCO_GRAPHICS_DRAW_INDEX
public ShadowVaryings vsMain<T : ISurface>(uint vertexId : SV_VertexID, uint drawId : SV_DrawIndex)
{
#else
public ShadowVaryings vsMain<T : ISurface>(uint vertexId : SV_VertexID)
{
    uint drawId = 0u;
#endif
    World3DShadowDrawRoot root = gpu_load_root<World3DShadowDrawRoot>();
    World3DView view = gpu_load<World3DView>(root.Views, root.ViewIndex);
    World3DDrawRef draw = gpu_load<World3DDrawRef>(root.DrawRefs, root.FirstDraw + drawId);
    World3DInstanceGpu instance = world3d_load_instance(root.Directory, draw.InstanceSlot);
    World3DGeometryLodGpu lod = world3d_load_geometry_lod(root.Directory, instance.GeometrySlot, draw.Lod);
    World3DVertexGpu vertex = world3d_load_vertex(gpu_offset_bytes(root.PayloadPage, lod.VertexByteOffset), vertexId);

    float3 eyeRelativePosition = world3d_eye_relative_position(instance.Spatial, view, vertex.Position);
    float3 normal = normalizeOr(mul(vertex.Normal, (float3x3)instance.Spatial.ObjectToCell), float3(0.0, 0.0, 1.0));

    T surface = T();
    ShadowVaryings output;
    output.EyeRelativePosition = surface.getWorldPosition(eyeRelativePosition, normal, vertex.UV);
    output.Position = mul(float4(output.EyeRelativePosition, 1.0), view.CameraVP);
    output.UV = vertex.UV;
    output.MaterialSlot = instance.MaterialSlot;
    output.Fade = instance.Fade;
    return output;
}

[shader("pixel")]
public void psMain<T : ISurface, let alphaTest : bool>(ShadowVaryings vertex)
{
    if (!alphaTest)
        return;

    World3DShadowDrawRoot root = gpu_load_root<World3DShadowDrawRoot>();
    SurfaceInput input;
    input.EyeRelativePosition = vertex.EyeRelativePosition;
    input.NormalWS = float3(0.0, 0.0, 1.0);
    input.TangentWS = float4(1.0, 0.0, 0.0, 1.0);
    input.UV = vertex.UV;
    input.DdxUV = ddx(vertex.UV);
    input.DdyUV = ddy(vertex.UV);
    input.Material = world3d_load_material(root.Directory, vertex.MaterialSlot);

    T surface = T();
    float4 baseColor = surface.getBaseColor(input);
    if (baseColor.a * vertex.Fade < input.Material.EmissiveAlphaCutoff.w)
        discard;
}
