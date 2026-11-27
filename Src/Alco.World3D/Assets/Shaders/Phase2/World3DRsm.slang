#language slang 2025
module World3DRsm;

import Core;
import World3DSceneData;
import World3DSurface;

[GPURoot]
public struct World3DRsmDrawRoot
{
    public GPUBufferView Directory;
    public GPUBufferView Views;
    public GPUBufferView DrawRefs;
    public GPUBufferView PayloadPage;
    public uint FirstDraw;
    public uint ViewIndex;
    public uint Reserved0;
    public uint Reserved1;
    public float4 SunDirectionAndIntensity;
    public float4 SunColorAndPadding;
};

struct World3DDrawRef
{
    uint InstanceSlot;
    uint Lod;
};

public struct RsmVaryings
{
    public float4 Position : SV_Position;
    public float3 EyeRelativePosition : TEXCOORD0;
    public float3 NormalWS : TEXCOORD1;
    public float4 TangentWS : TEXCOORD2;
    public float2 UV : TEXCOORD3;
    public nointerpolation uint MaterialSlot : TEXCOORD4;
    public nointerpolation float Fade : TEXCOORD5;
    public nointerpolation uint Mirrored : TEXCOORD6;
};

public struct RsmOutput
{
    public float4 Flux : SV_Target0;
    public float4 Normal : SV_Target1;
};

float3 normalizeOr(float3 direction, float3 fallback)
{
    float lengthSquared = dot(direction, direction);
    return lengthSquared > 0.0 && isfinite(lengthSquared) ? direction * rsqrt(lengthSquared) : fallback;
}

float3 transformNormal(float3 normal, float3x3 linear, out bool mirrored)
{
    float3 cofactorX = cross(linear[1], linear[2]);
    float3 cofactorY = cross(linear[2], linear[0]);
    float3 cofactorZ = cross(linear[0], linear[1]);
    mirrored = dot(linear[0], cofactorX) < 0.0;
    float3 transformed = normal.x * cofactorX + normal.y * cofactorY + normal.z * cofactorZ;
    return normalizeOr(mirrored ? -transformed : transformed, float3(0.0, 0.0, 1.0));
}

[shader("vertex")]
#ifdef ALCO_GRAPHICS_DRAW_INDEX
public RsmVaryings vsMain<T : ISurface>(uint vertexId : SV_VertexID, uint drawId : SV_DrawIndex)
{
#else
public RsmVaryings vsMain<T : ISurface>(uint vertexId : SV_VertexID)
{
    uint drawId = 0u;
#endif
    World3DRsmDrawRoot root = gpu_load_root<World3DRsmDrawRoot>();
    World3DView view = gpu_load<World3DView>(root.Views, root.ViewIndex);
    World3DDrawRef draw = gpu_load<World3DDrawRef>(root.DrawRefs, root.FirstDraw + drawId);
    World3DInstanceGpu instance = world3d_load_instance(root.Directory, draw.InstanceSlot);
    World3DGeometryLodGpu lod = world3d_load_geometry_lod(root.Directory, instance.GeometrySlot, draw.Lod);
    World3DVertexGpu vertex = world3d_load_vertex(gpu_offset_bytes(root.PayloadPage, lod.VertexByteOffset), vertexId);

    float3 eyeRelativePosition = world3d_eye_relative_position(instance.Spatial, view, vertex.Position);
    float3x3 linear = (float3x3)instance.Spatial.ObjectToCell;
    bool mirrored;
    float3 normal = transformNormal(vertex.Normal, linear, mirrored);
    float3 tangent = mul(vertex.Tangent.xyz, linear);

    T surface = T();
    RsmVaryings output;
    output.EyeRelativePosition = surface.getWorldPosition(eyeRelativePosition, normal, vertex.UV);
    output.Position = mul(float4(output.EyeRelativePosition, 1.0), view.CameraVP);
    output.NormalWS = surface.getWorldNormal(eyeRelativePosition, normal, vertex.UV);
    output.TangentWS = float4(tangent, mirrored ? -vertex.Tangent.w : vertex.Tangent.w);
    output.UV = vertex.UV;
    output.MaterialSlot = instance.MaterialSlot;
    output.Fade = instance.Fade;
    output.Mirrored = mirrored ? 1u : 0u;
    return output;
}

[shader("pixel")]
public RsmOutput psMain<T : ISurface, let alphaTest : bool>(RsmVaryings vertex, bool front : SV_IsFrontFace)
{
    World3DRsmDrawRoot root = gpu_load_root<World3DRsmDrawRoot>();
    SurfaceInput input;
    input.EyeRelativePosition = vertex.EyeRelativePosition;
    input.UV = vertex.UV;
    input.DdxUV = ddx(vertex.UV);
    input.DdyUV = ddy(vertex.UV);
    input.Material = world3d_load_material(root.Directory, vertex.MaterialSlot);

    float3 n = normalizeOr(vertex.NormalWS, float3(0.0, 0.0, 1.0));
    float3 t = normalizeOr(vertex.TangentWS.xyz - n * dot(n, vertex.TangentWS.xyz), float3(1.0, 0.0, 0.0));
    float handedness = vertex.TangentWS.w < 0.0 ? -1.0 : 1.0;
    if ((input.Material.Flags & WORLD3D_MATERIAL_DOUBLE_SIDED) != 0u && front != (vertex.Mirrored != 0u))
    {
        n = -n;
        t = -t;
        handedness = -handedness;
    }
    input.NormalWS = n;
    input.TangentWS = float4(t, handedness);

    T surface = T();
    float4 baseColor = surface.getBaseColor(input);
    if (alphaTest && baseColor.a * vertex.Fade < input.Material.EmissiveAlphaCutoff.w)
        discard;

    float3 mrAO = surface.getMetallicRoughnessAO(input);
    float3 normalTS = surface.getNormalTS(input);
    float3 b = cross(n, t) * handedness;
    n = normalizeOr(t * normalTS.x + b * normalTS.y + n * normalTS.z, n);

    float ndotL = saturate(dot(n, root.SunDirectionAndIntensity.xyz));
    float3 irradiance = root.SunColorAndPadding.rgb * root.SunDirectionAndIntensity.w;
    float3 flux = baseColor.rgb * (1.0 - mrAO.x) * (1.0 / 3.14159265) * ndotL * irradiance + surface.getEmissive(input);

    RsmOutput output;
    output.Flux = float4(flux, 1.0);
    output.Normal = float4(n * 0.5 + 0.5, 1.0);
    return output;
}
