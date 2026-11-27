#language slang 2025
module World3DSceneData;

import Core;

// Directory windows are measured in GPUBufferView entries, not bytes.
public static const uint WORLD3D_TABLE_PAGE_SIZE = 256u;
public static const uint WORLD3D_DIRECTORY_PAGE_COUNT = 1024u;
public static const uint WORLD3D_MAX_TABLE_SLOTS = WORLD3D_TABLE_PAGE_SIZE * WORLD3D_DIRECTORY_PAGE_COUNT;
public static const uint WORLD3D_MAX_GEOMETRY_LODS = 4u;
public static const uint WORLD3D_INSTANCE_WINDOW = 0u;
public static const uint WORLD3D_MATERIAL_WINDOW = WORLD3D_DIRECTORY_PAGE_COUNT;
public static const uint WORLD3D_GEOMETRY_WINDOW = 2u * WORLD3D_DIRECTORY_PAGE_COUNT;
public static const uint WORLD3D_GEOMETRY_PAYLOAD_WINDOW = 3u * WORLD3D_DIRECTORY_PAGE_COUNT;
public static const float WORLD3D_CELL_SIZE = 512.0;

public static const uint WORLD3D_INSTANCE_LIVE = 1u;
public static const uint WORLD3D_INSTANCE_CAST_SHADOW = 2u;
public static const uint WORLD3D_MATERIAL_ALPHA_MODE_MASK = 3u;
public static const uint WORLD3D_MATERIAL_DOUBLE_SIDED = 4u;

public static const uint WORLD3D_CULL_VIEW_ENABLED = 1u;
public static const uint WORLD3D_CULL_VIEW_ORTHOGRAPHIC = 2u;
public static const uint WORLD3D_CULL_VIEW_SHADOW_CASTER_ONLY = 4u;
public static const uint WORLD3D_CULL_VIEW_HIZ_OCCLUSION = 8u;
public static const uint WORLD3D_CULL_VIEW_USE_MAX_DISTANCE = 16u;
public static const uint WORLD3D_CULL_VIEW_IS_PRIMARY = 32u;

public struct World3DInstanceSpatialGpu
{
    public row_major float4x4 ObjectToCell;
    public int4 Cell;
    public float4 BoundsCenterRadius;
    public float4 BoundsExtent;
};

public struct World3DView
{
    public row_major float4x4 CameraVP;
    public row_major float4x4 CameraInvVP;
    public int4 EyeCell;
    public int4 OriginAnchorCell;
    public float4 EyeLocalNear;
    public float4 ViewportSizeInvSize;
};

public struct World3DCullViewGpu
{
    public row_major float4x4 ViewProj;
    public int4 EyeCell;
    public float4 EyeLocalNear;
    public uint TargetViewSlot;
    public uint Flags;
    public float MinObjectRadius;
    public float MinTexelLodBiasMeters;
    public float MaxDistance;
    public uint Reserved0;
    public uint Reserved1;
    public uint Reserved2;
};

public struct World3DOccludedCandidate
{
    public uint InstanceSlot;
    public uint Lod;
};

public struct World3DInstanceGpu
{
    public World3DInstanceSpatialGpu Spatial;
    public uint GeometrySlot;
    public uint MaterialSlot;
    public uint Flags;
    public float Fade;
};

public struct World3DMaterialGpu
{
    public float4 BaseColor;
    public float4 EmissiveAlphaCutoff;
    public float2 MetallicRoughness;
    public uint Flags;
    public GPUSamplerId Sampler;
    public GPUTextureId BaseColorTexture;
    public GPUTextureId NormalTexture;
    public GPUTextureId MetallicRoughnessTexture;
    public GPUTextureId EmissiveTexture;
};

// Offsets are relative to the geometry payload page. A zero index count marks an absent LOD.
public struct World3DGeometryLodGpu
{
    public uint PageId;
    public uint VertexByteOffset;
    public uint IndexByteOffset;
    public uint VertexCount;
    public uint IndexCount;
    public float MinimumDistance;
    public uint Reserved0;
    public uint Reserved1;
    public float4 LocalBoundsMin;
    public float4 LocalBoundsMax;
};

// Geometry payloads contain tightly packed VertexPBR vertices and geometry-local UInt32 indices.
public struct World3DVertexGpu
{
    public float3 Position;
    public float3 Normal;
    public float2 UV;
    public float4 Tangent;
};

// Typed descriptions of the backing arrays; consumers load individual entries, not whole pages.
public struct World3DPageDirectory
{
    public GPUBufferView Instances[WORLD3D_DIRECTORY_PAGE_COUNT];
    public GPUBufferView Materials[WORLD3D_DIRECTORY_PAGE_COUNT];
    public GPUBufferView Geometry[WORLD3D_DIRECTORY_PAGE_COUNT];
    public GPUBufferView GeometryPayload[WORLD3D_DIRECTORY_PAGE_COUNT];
};

public struct World3DInstancePage
{
    public World3DInstanceGpu Rows[WORLD3D_TABLE_PAGE_SIZE];
};

public struct World3DMaterialPage
{
    public World3DMaterialGpu Rows[WORLD3D_TABLE_PAGE_SIZE];
};

public struct World3DGeometryRow
{
    public World3DGeometryLodGpu Lods[WORLD3D_MAX_GEOMETRY_LODS];
};

public struct World3DGeometryPage
{
    public World3DGeometryRow Rows[WORLD3D_TABLE_PAGE_SIZE];
};

[ForceInline]
GPUBufferView world3d_lookup_page(GPUBufferView directory, uint window, uint pageId)
{
    if (directory.Index == 0u || pageId >= WORLD3D_DIRECTORY_PAGE_COUNT)
        return (GPUBufferView)0;
    return gpu_load<GPUBufferView>(directory, window + pageId);
}

[ForceInline]
public World3DInstanceGpu world3d_load_instance(GPUBufferView directory, uint slot)
{
    GPUBufferView page = world3d_lookup_page(directory, WORLD3D_INSTANCE_WINDOW, slot / WORLD3D_TABLE_PAGE_SIZE);
    if (page.Index == 0u)
        return (World3DInstanceGpu)0;
    return gpu_load<World3DInstanceGpu>(page, slot % WORLD3D_TABLE_PAGE_SIZE);
}

// Only read material slots referenced by live instances; a zero material is not a liveness flag.
[ForceInline]
public World3DMaterialGpu world3d_load_material(GPUBufferView directory, uint slot)
{
    GPUBufferView page = world3d_lookup_page(directory, WORLD3D_MATERIAL_WINDOW, slot / WORLD3D_TABLE_PAGE_SIZE);
    if (page.Index == 0u)
        return (World3DMaterialGpu)0;
    return gpu_load<World3DMaterialGpu>(page, slot % WORLD3D_TABLE_PAGE_SIZE);
}

[ForceInline]
public World3DGeometryLodGpu world3d_load_geometry_lod(GPUBufferView directory, uint slot, uint lod)
{
    GPUBufferView page = world3d_lookup_page(directory, WORLD3D_GEOMETRY_WINDOW, slot / WORLD3D_TABLE_PAGE_SIZE);
    if (page.Index == 0u || lod >= WORLD3D_MAX_GEOMETRY_LODS)
        return (World3DGeometryLodGpu)0;
    uint entry = (slot % WORLD3D_TABLE_PAGE_SIZE) * WORLD3D_MAX_GEOMETRY_LODS + lod;
    return gpu_load<World3DGeometryLodGpu>(page, entry);
}

[ForceInline]
public bool world3d_try_get_geometry_views(GPUBufferView directory, World3DGeometryLodGpu lod,
    out GPUBufferView vertices, out GPUBufferView indices)
{
    vertices = (GPUBufferView)0;
    indices = (GPUBufferView)0;
    if (lod.IndexCount == 0u)
        return false;
    GPUBufferView page = world3d_lookup_page(directory, WORLD3D_GEOMETRY_PAYLOAD_WINDOW, lod.PageId);
    if (page.Index == 0u)
        return false;
    vertices = gpu_offset_bytes(page, lod.VertexByteOffset);
    indices = gpu_offset_bytes(page, lod.IndexByteOffset);
    return true;
}

[ForceInline]
public World3DVertexGpu world3d_load_vertex(GPUBufferView vertices, uint vertex)
{
    return gpu_load<World3DVertexGpu>(vertices, vertex);
}

[ForceInline]
public uint world3d_load_index(GPUBufferView indices, uint index)
{
    return gpu_load<uint>(indices, index);
}

[ForceInline]
public float3 world3d_eye_relative_pivot_from_cell(World3DInstanceSpatialGpu spatial, int4 eyeCell, float3 eyeLocal)
{
    // Subtract cells in integer space before converting to float. The origin anchor does not enter eye-relative math.
    return float3(spatial.Cell.xyz - eyeCell.xyz) * WORLD3D_CELL_SIZE
        + (spatial.ObjectToCell[3].xyz - eyeLocal);
}

[ForceInline]
public float3 world3d_eye_relative_pivot(World3DInstanceSpatialGpu spatial, World3DView view)
{
    return world3d_eye_relative_pivot_from_cell(spatial, view.EyeCell, view.EyeLocalNear.xyz);
}

[ForceInline]
public float3 world3d_eye_relative_position(World3DInstanceSpatialGpu spatial, World3DView view, float3 localPosition)
{
    return mul(float4(localPosition, 0.0), spatial.ObjectToCell).xyz + world3d_eye_relative_pivot(spatial, view);
}
