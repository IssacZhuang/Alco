using System.Numerics;

namespace Alco.World3D;

/// <summary>Identifies a geometry in its originating scene.</summary>
/// <param name="SceneId">The owning scene.</param>
/// <param name="Slot">The scene-local slot.</param>
/// <param name="Generation">The slot generation; zero is invalid.</param>
public readonly record struct WorldGeometryHandle(Guid SceneId, int Slot, uint Generation);

/// <summary>Identifies a material in its originating scene.</summary>
/// <param name="SceneId">The owning scene.</param>
/// <param name="Slot">The scene-local slot.</param>
/// <param name="Generation">The slot generation; zero is invalid.</param>
public readonly record struct WorldMaterialHandle(Guid SceneId, int Slot, uint Generation);

/// <summary>Identifies an instance in its originating scene.</summary>
/// <param name="SceneId">The owning scene.</param>
/// <param name="Slot">The scene-local slot.</param>
/// <param name="Generation">The slot generation; zero is invalid.</param>
public readonly record struct WorldInstanceHandle(Guid SceneId, int Slot, uint Generation);

/// <summary>Identifies a group of instances that can be unloaded together.</summary>
/// <param name="SceneId">The owning scene.</param>
/// <param name="Slot">The scene-local slot.</param>
/// <param name="Generation">The slot generation; zero is invalid.</param>
public readonly record struct WorldSectorHandle(Guid SceneId, int Slot, uint Generation);

/// <summary>Identifies a scene-owned sampled texture.</summary>
/// <param name="SceneId">The owning scene.</param>
/// <param name="Slot">The scene-local slot.</param>
/// <param name="Generation">The slot generation; zero is invalid.</param>
public readonly record struct WorldTextureHandle(Guid SceneId, int Slot, uint Generation);

/// <summary>Determines how a standard world material handles opacity.</summary>
public enum World3DAlphaMode : uint
{
    /// <summary>Discards no fragments based on alpha.</summary>
    Opaque,
    /// <summary>Discards fragments below the alpha cutoff.</summary>
    Mask,
    /// <summary>Routes the material to alpha-blended rendering.</summary>
    Blend,
}

/// <summary>Selects a sampler from the rendering system's shared sampler bank.</summary>
public enum World3DMaterialSampler
{
    /// <summary>Linear filtering with repeat addressing.</summary>
    LinearRepeat,
    /// <summary>Linear filtering with clamp addressing.</summary>
    LinearClamp,
    /// <summary>Nearest filtering with repeat addressing.</summary>
    NearestRepeat,
    /// <summary>Nearest filtering with clamp addressing.</summary>
    NearestClamp,
    /// <summary>Anisotropic filtering with repeat addressing.</summary>
    AnisotropicRepeat,
    /// <summary>Anisotropic filtering with clamp addressing.</summary>
    AnisotropicClamp,
}

/// <summary>Describes one entry in the standard bindless material table.</summary>
public readonly record struct World3DMaterialDesc
{
    /// <summary>Creates a white, rough, opaque material with no authored textures.</summary>
    public World3DMaterialDesc() { }

    /// <summary>The linear color and opacity factors.</summary>
    public Vector4 BaseColor { get; init; } = Vector4.One;
    /// <summary>The linear emissive factor.</summary>
    public Vector3 Emissive { get; init; }
    /// <summary>The metallic factor in [0, 1].</summary>
    public float Metallic { get; init; }
    /// <summary>The roughness factor in [0, 1].</summary>
    public float Roughness { get; init; } = 1;
    /// <summary>The alpha masking threshold in [0, 1].</summary>
    public float AlphaCutoff { get; init; } = 0.5f;
    /// <summary>The opacity routing mode.</summary>
    public World3DAlphaMode AlphaMode { get; init; }
    /// <summary>Whether both triangle faces render.</summary>
    public bool DoubleSided { get; init; }
    /// <summary>The shared sampler used for this material's textures.</summary>
    public World3DMaterialSampler Sampler { get; init; }
    /// <summary>The optional base color texture; the default handle uses white.</summary>
    public WorldTextureHandle BaseColorTexture { get; init; }
    /// <summary>The optional normal texture; the default handle selects a flat normal.</summary>
    public WorldTextureHandle NormalTexture { get; init; }
    /// <summary>The optional metallic-roughness texture; the default handle uses white.</summary>
    public WorldTextureHandle MetallicRoughnessTexture { get; init; }
    /// <summary>The optional emissive texture; the default handle has no emissive map.</summary>
    public WorldTextureHandle EmissiveTexture { get; init; }
    /// <summary>The Slang module exporting an ISurface implementation, or null for the standard PBR surface.</summary>
    public string? SurfaceModule { get; init; }
}

/// <summary>Describes an instance's resources, placement, and sector ownership.</summary>
/// <param name="Sector">The sector that unloads this instance.</param>
/// <param name="Geometry">The geometry whose LODs the instance can draw.</param>
/// <param name="Material">The instance's material.</param>
/// <param name="Transform">The authoritative double-position transform.</param>
/// <param name="Fade">The authored opacity multiplier in [0, 1].</param>
public readonly record struct World3DInstanceDesc(WorldSectorHandle Sector, WorldGeometryHandle Geometry,
    WorldMaterialHandle Material, World3DTransform Transform, float Fade = 1);

/// <summary>Counts persistent scene uploads recorded in a frame, excluding per-view data.</summary>
/// <param name="GeometryBytes">Bytes of vertex and index payload copied.</param>
/// <param name="GeometryPatches">Dirty geometry table entries.</param>
/// <param name="MaterialPatches">Dirty material table entries.</param>
/// <param name="InstancePatches">Dirty instance table entries.</param>
/// <param name="InitializedTablePages">New GPU table pages initialized.</param>
public readonly record struct World3DSceneSyncStats(ulong GeometryBytes, int GeometryPatches,
    int MaterialPatches, int InstancePatches, int InitializedTablePages);

/// <summary>Counts GBuffer submissions before GPU culling determines which draws execute.</summary>
/// <param name="MdiCalls">The number of indexed indirect API calls.</param>
/// <param name="IndirectSlots">The total submitted commands, including commands with zero instances.</param>
public readonly record struct World3DDrawStats(int MdiCalls, int IndirectSlots);
