#language slang 2025
module AlcoRendering_MeshScene3D;

import Core;
import SharedSampler;

public static const uint MESH_SCENE3D_SAMPLER_MASK = 3u;
public static const uint MESH_SCENE3D_SAMPLER_LINEAR_REPEAT = 0u;
public static const uint MESH_SCENE3D_SAMPLER_LINEAR_CLAMP = 1u;
public static const uint MESH_SCENE3D_SAMPLER_NEAREST_REPEAT = 2u;
public static const uint MESH_SCENE3D_SAMPLER_NEAREST_CLAMP = 3u;

public static const uint MESH_SCENE3D_FLAG_PREMULTIPLIED = 1u << 2;

// Matches Alco.Rendering.VertexPBR under ForceCLayout (48 bytes).
public struct MeshScene3DVertex
{
    public float3 Position;
    public float3 Normal;
    public float2 Uv;
    public float4 Tangent;
};

// Generic 3D instanced payload stored in MeshScene3D (128 bytes).
public struct MeshScene3DInstance
{
    public float4 World0;      // (M11, M21, M31, M41)
    public float4 World1;      // (M12, M22, M32, M42)
    public float4 World2;      // (M13, M23, M33, M43)
    public float4 Color;
    public float4 CustomData0;
    public float4 CustomData1;
    public uint TextureId;
    public uint AuxTextureId;
    public uint MaterialParamId;
    public uint Flags;         // bits 0..1 sampler, bit 2 premultiplied, bits 3..31 caller flags
    public uint UserData0;
    public uint UserData1;
    public uint CullMask;
    public uint Reserved;
};

// Per-bucket GPU metadata read by reset and culling compute shaders (48 bytes).
public struct MeshScene3DBucketMeta
{
    public uint IndexCount;
    public uint VisibleBase;
    public uint BucketTag;
    public uint FirstIndex;
    public float3 LocalCenter;
    public int VertexOffset;
    public float3 LocalExtent;
    public uint Pad0;
};

// Root header placed at offset 0 of every MeshScene3D graphics material (144 bytes).
public struct MeshScene3DPassHeader
{
    public float4x4 ViewProjection;
    public GPUBufferView Vertices;
    public GPUBufferView Pool;
    public GPUBufferView VisibleSlots;
    public GPUBufferView MaterialParams;
    public float4 UserParams;
    public float Time;
    public uint BucketTag;
    public uint HasMaterialParams;
    public uint PassFlags;
    public float3 OriginOffset;
    public uint Pad0;
};

public struct EmptyMeshScene3DParams
{
    public uint _pad;
};

public struct MeshScene3DVertexInput
{
    public uint VertexId;
    public uint InstanceId;
    public uint Slot;
    public MeshScene3DVertex Vertex;
    public MeshScene3DInstance Instance;
    public float Time;
    public uint BucketTag;
    public uint PassFlags;
    public float4 UserParams;
    public GPUBufferView MaterialParams;
    public uint HasMaterialParams;
};

public struct MeshScene3DTransformedVertex
{
    public float3 WorldPos;
    public float3 WorldNormal;
    public float2 Uv;
};

public struct MeshScene3DSurfaceInput
{
    public float3 WorldPos;
    public float3 WorldNormal;
    public float2 Uv;
    public float2 ScreenPosition;
    public float2 DdxUv;
    public float2 DdyUv;
    public float4 Color;
    public float4 CustomData0;
    public float4 CustomData1;
    public uint Slot;
    public uint TextureId;
    public uint AuxTextureId;
    public uint MaterialParamId;
    public uint Flags;
    public uint UserData0;
    public uint UserData1;
    public uint BucketTag;
    public uint PassFlags;
    public float Time;
    public float4 UserParams;
    public GPUBufferView MaterialParams;
    public uint HasMaterialParams;
};

public struct MeshScene3DVsOut
{
    public float4 Position : SV_Position;
    public float3 WorldPos : TEXCOORD0;
    public float3 WorldNormal : TEXCOORD1;
    public float2 Uv : TEXCOORD2;
    public float4 Color : COLOR0;
    public float4 CustomData0 : TEXCOORD3;
    public float4 CustomData1 : TEXCOORD4;
    public nointerpolation uint Slot : TEXCOORD5;
    public nointerpolation uint TextureId : TEXCOORD6;
    public nointerpolation uint AuxTextureId : TEXCOORD7;
    public nointerpolation uint Flags : TEXCOORD8;
    public nointerpolation uint MaterialParamId : TEXCOORD9;
    public nointerpolation uint UserData0 : TEXCOORD10;
    public nointerpolation uint UserData1 : TEXCOORD11;
};

public interface IMeshScene3DSurface
{
    associatedtype Params;

    MeshScene3DTransformedVertex TransformVertex(MeshScene3DVertexInput input, Params p)
    {
        MeshScene3DTransformedVertex result;
        result.WorldPos = meshScene3DTransformPosition(input.Instance, input.Vertex.Position);
        result.WorldNormal = meshScene3DTransformNormal(input.Instance, input.Vertex.Normal);
        result.Uv = input.Vertex.Uv;
        return result;
    }

    float4 GetColor(MeshScene3DSurfaceInput input, Params p);

    float GetAlphaCutoff(Params p)
    {
        return 0.5;
    }
};

public MeshScene3DVertex meshScene3DLoadVertex(GPUBufferView vertices, uint vertexId)
{
    return gpu_load<MeshScene3DVertex>(vertices, vertexId);
}

public uint meshScene3DLoadSlot(GPUBufferView visibleSlots, uint instanceId)
{
    return gpu_load<uint>(visibleSlots, instanceId);
}

public MeshScene3DInstance meshScene3DLoadInstance(GPUBufferView pool, uint slot)
{
    return gpu_load<MeshScene3DInstance>(pool, slot);
}

public float3 meshScene3DTransformPosition(MeshScene3DInstance inst, float3 localPos)
{
    float4 p = float4(localPos, 1.0);
    return float3(dot(p, inst.World0), dot(p, inst.World1), dot(p, inst.World2));
}

public float3 meshScene3DTransformPositionRelative(MeshScene3DInstance inst, float3 localPos, float3 originOffset)
{
    float3 relTranslation = float3(
        inst.World0.w - originOffset.x,
        inst.World1.w - originOffset.y,
        inst.World2.w - originOffset.z);
    return float3(
        dot(localPos, inst.World0.xyz) + relTranslation.x,
        dot(localPos, inst.World1.xyz) + relTranslation.y,
        dot(localPos, inst.World2.xyz) + relTranslation.z);
}

// Transforms a local normal using the 3x3 cofactor matrix of the affine linear part,
// preserving outward orientation under rotation and non-uniform scaling.
public float3 meshScene3DTransformNormal(MeshScene3DInstance inst, float3 localNormal)
{
    float3 r0 = float3(inst.World0.x, inst.World1.x, inst.World2.x);
    float3 r1 = float3(inst.World0.y, inst.World1.y, inst.World2.y);
    float3 r2 = float3(inst.World0.z, inst.World1.z, inst.World2.z);

    float3 c0 = cross(r1, r2);
    float3 c1 = cross(r2, r0);
    float3 c2 = cross(r0, r1);

    float3 n = localNormal.x * c0 + localNormal.y * c1 + localNormal.z * c2;
    float lenSq = dot(n, n);
    return lenSq > 1e-12 ? n * rsqrt(lenSq) : float3(0.0, 0.0, 1.0);
}

public float4 meshScene3DSampleTexture(uint textureId, uint flags, float2 uv, float2 ddxUv, float2 ddyUv)
{
    Texture2D<float4> tex = gpu_texture<Texture2D<float4>>(gpu_nonuniform_index(textureId));
    uint samplerMode = flags & MESH_SCENE3D_SAMPLER_MASK;
    if (samplerMode == MESH_SCENE3D_SAMPLER_LINEAR_CLAMP)
        return tex.SampleGrad(shared_sampler_linear_clamp(), uv, ddxUv, ddyUv);
    if (samplerMode == MESH_SCENE3D_SAMPLER_NEAREST_REPEAT)
        return tex.SampleGrad(shared_sampler_nearest_repeat(), uv, ddxUv, ddyUv);
    if (samplerMode == MESH_SCENE3D_SAMPLER_NEAREST_CLAMP)
        return tex.SampleGrad(shared_sampler_nearest_clamp(), uv, ddxUv, ddyUv);
    return tex.SampleGrad(shared_sampler_linear_repeat(), uv, ddxUv, ddyUv);
}
