#language slang 2025
module World3DGBuffer;

import Core;
import World3DSceneData;
import World3DSurface;

[GPURoot]
public struct World3DGBufferRoot
{
    public GPUBufferView Directory;
    public GPUBufferView Views;
    public GPUBufferView DrawRefs;
    public GPUBufferView PayloadPage;
    public uint FirstDraw;
    public uint ViewIndex;
};

// Parallel to the indexed indirect argument stream; one eight-byte record per draw.
struct World3DDrawRef
{
    uint InstanceSlot;
    uint Lod;
};

public struct GBufferVaryings
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

public struct GBufferOutput
{
    public float4 Albedo : SV_Target0;
    public float4 Normal : SV_Target1;
    public float4 MetallicRoughnessAO : SV_Target2;
    public float4 Emissive : SV_Target3;
};

float3 normalizeOr(float3 direction, float3 fallback)
{
    float lengthSquared = dot(direction, direction);
    return lengthSquared > 0.0 && isfinite(lengthSquared) ? direction * rsqrt(lengthSquared) : fallback;
}

// The cofactor rows are det(M) * M^-T. Correct the sign before normalization
// so mirrored transforms retain the outward normal and flip the tangent frame sign.
float3 transformNormal(float3 normal, float3x3 linear, out bool mirrored)
{
    float3 cofactorX = cross(linear[1], linear[2]);
    float3 cofactorY = cross(linear[2], linear[0]);
    float3 cofactorZ = cross(linear[0], linear[1]);
    mirrored = dot(linear[0], cofactorX) < 0.0;
    float3 transformed = normal.x * cofactorX + normal.y * cofactorY + normal.z * cofactorZ;
    return normalizeOr(mirrored ? -transformed : transformed, float3(0.0, 0.0, 1.0));
}

float3 orthogonalizeTangent(float3 tangent, float3 normal)
{
    tangent -= normal * dot(normal, tangent);
    float lengthSquared = dot(tangent, tangent);
    if (lengthSquared > 0.0 && isfinite(lengthSquared))
        return tangent * rsqrt(lengthSquared);

    float3 axis = abs(normal.z) < 0.999 ? float3(0.0, 0.0, 1.0) : float3(0.0, 1.0, 0.0);
    return normalize(cross(axis, normal));
}

// The albedo target is UNorm, not hardware sRGB; lighting explicitly decodes this RGB.
float3 encodeSRGB(float3 color)
{
    float3 lo = color * 12.92;
    float3 hi = 1.055 * pow(max(color, 0.0), float3(1.0 / 2.4)) - 0.055;
    return lerp(hi, lo, step(color, float3(0.0031308)));
}

// Preserve the legacy octahedral geometry normal in the normal/emissive alpha channels.
float2 encodeGeometryNormal(float3 normal)
{
    normal /= max(abs(normal.x) + abs(normal.y) + abs(normal.z), 0.0001);
    float2 encoded = normal.xy;
    if (normal.z < 0.0)
    {
        encoded = (1.0 - abs(encoded.yx))
            * float2(encoded.x >= 0.0 ? 1.0 : -1.0, encoded.y >= 0.0 ? 1.0 : -1.0);
    }
    return encoded * 0.5 + 0.5;
}

[shader("vertex")]
#ifdef ALCO_GRAPHICS_DRAW_INDEX
public GBufferVaryings vsMain<T : ISurface>(uint vertexId : SV_VertexID, uint drawId : SV_DrawIndex)
{
#else
public GBufferVaryings vsMain<T : ISurface>(uint vertexId : SV_VertexID)
{
    // The caller submits one indirect record at a time on backends without DrawIndex.
    uint drawId = 0u;
#endif
    World3DGBufferRoot root = gpu_load_root<World3DGBufferRoot>();
    World3DView view = gpu_load<World3DView>(root.Views, root.ViewIndex);
    World3DDrawRef draw = gpu_load<World3DDrawRef>(root.DrawRefs, root.FirstDraw + drawId);
    World3DInstanceGpu instance = world3d_load_instance(root.Directory, draw.InstanceSlot);
    World3DGeometryLodGpu lod = world3d_load_geometry_lod(root.Directory, instance.GeometrySlot, draw.Lod);
    // The hardware fetched a geometry-local UInt32 index from this same payload page.
    World3DVertexGpu vertex = world3d_load_vertex(gpu_offset_bytes(root.PayloadPage, lod.VertexByteOffset), vertexId);

    float3 eyeRelativePosition = world3d_eye_relative_position(instance.Spatial, view, vertex.Position);
    float3x3 linear = (float3x3)instance.Spatial.ObjectToCell;
    bool mirrored;
    float3 normal = transformNormal(vertex.Normal, linear, mirrored);
    float3 tangent = mul(vertex.Tangent.xyz, linear);

    T surface = T();
    GBufferVaryings output;
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
public GBufferOutput psMain<T : ISurface, let alphaTest : bool>(GBufferVaryings vertex, bool front : SV_IsFrontFace)
{
    World3DGBufferRoot root = gpu_load_root<World3DGBufferRoot>();
    SurfaceInput input;
    input.EyeRelativePosition = vertex.EyeRelativePosition;
    input.UV = vertex.UV;
    input.DdxUV = ddx(vertex.UV);
    input.DdyUV = ddy(vertex.UV);
    input.Material = world3d_load_material(root.Directory, vertex.MaterialSlot);

    float3 n = normalizeOr(vertex.NormalWS, float3(0.0, 0.0, 1.0));
    float3 t = orthogonalizeTangent(vertex.TangentWS.xyz, n);
    float handedness = vertex.TangentWS.w < 0.0 ? -1.0 : 1.0;
    // World3D geometry faces clockwise in screen space, whereas SV_IsFrontFace
    // reports CCW. Mirroring swaps the authored front and back winding.
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
    float3 normal = normalizeOr(t * normalTS.x + b * normalTS.y + n * normalTS.z, n);
    float2 geometryNormal = encodeGeometryNormal(n);

    GBufferOutput output;
    output.Albedo = float4(encodeSRGB(baseColor.rgb), mrAO.y);
    output.Normal = float4(normal * 0.5 + 0.5, geometryNormal.x);
    output.MetallicRoughnessAO = float4(mrAO, 1.0);
    output.Emissive = float4(surface.getEmissive(input), geometryNormal.y);
    return output;
}
