using System.Numerics;
using Alco.Rendering;

namespace Alco.Engine;

/// <summary>
/// A material of a loaded 3D model scene, metallic-roughness workflow subset.
/// </summary>
public sealed class ModelMaterial
{
    /// <summary>The material name.</summary>
    public required string Name { get; init; }

    /// <summary>Linear base color factor, multiplied with the albedo texture.</summary>
    public Vector4 BaseColorFactor { get; init; } = Vector4.One;

    /// <summary>Metallic factor in [0, 1].</summary>
    public float MetallicFactor { get; init; }

    /// <summary>Roughness factor in [0, 1].</summary>
    public float RoughnessFactor { get; init; } = 1.0f;

    /// <summary>
    /// The albedo (base color) texture, null when the material has none or the texture
    /// failed to load (the pipeline binds its white fallback).
    /// </summary>
    public Texture2D? AlbedoTexture { get; set; }

    /// <summary>
    /// The normal map texture (tangent space), null when the material has none or the
    /// texture failed to load (the pipeline binds its flat-normal fallback).
    /// </summary>
    public Texture2D? NormalTexture { get; set; }

    /// <summary>
    /// The metallic-roughness texture (roughness in G, metallic in B), null when the
    /// material has none or the texture failed to load (the pipeline binds its white
    /// fallback, i.e. the factors pass through).
    /// </summary>
    public Texture2D? MetallicRoughnessTexture { get; set; }

    /// <summary>Linear emissive color factor, multiplied with the emissive texture.</summary>
    public Vector3 EmissiveFactor { get; init; } = Vector3.Zero;

    /// <summary>
    /// The emissive texture, null when the material has none or the texture failed to
    /// load (no emission).
    /// </summary>
    public Texture2D? EmissiveTexture { get; set; }

    /// <summary>The alpha handling mode.</summary>
    public GltfAlphaMode AlphaMode { get; init; }

    /// <summary>Alpha cutoff for <see cref="GltfAlphaMode.Mask"/>.</summary>
    public float AlphaCutoff { get; init; } = 0.5f;

    /// <summary>Whether both faces of triangles are rendered.</summary>
    public bool DoubleSided { get; init; }
}

/// <summary>
/// One renderable instance of a <see cref="ModelScene"/>: a mesh, a material index and
/// a world transform.
/// </summary>
public readonly struct ModelDrawItem
{
    /// <summary>The mesh to draw.</summary>
    public PrimitiveMesh Mesh { get; }

    /// <summary>Index into <see cref="ModelScene.Materials"/>.</summary>
    public int MaterialIndex { get; }

    /// <summary>The world transform.</summary>
    public Matrix4x4 World { get; }

    /// <summary>The minimum corner of the mesh-local bounds.</summary>
    public Vector3 LocalBoundsMin { get; }

    /// <summary>The maximum corner of the mesh-local bounds.</summary>
    public Vector3 LocalBoundsMax { get; }

    /// <summary>Create a draw item.</summary>
    /// <param name="mesh">The mesh to draw.</param>
    /// <param name="materialIndex">The material index.</param>
    /// <param name="world">The local-to-world transform.</param>
    /// <param name="localBoundsMin">The minimum corner of the mesh-local bounds.</param>
    /// <param name="localBoundsMax">The maximum corner of the mesh-local bounds.</param>
    public ModelDrawItem(
        PrimitiveMesh mesh,
        int materialIndex,
        in Matrix4x4 world,
        in Vector3 localBoundsMin,
        in Vector3 localBoundsMax)
    {
        Mesh = mesh;
        MaterialIndex = materialIndex;
        World = world;
        LocalBoundsMin = localBoundsMin;
        LocalBoundsMax = localBoundsMax;
    }
}

/// <summary>
/// A loaded 3D model scene: GPU meshes and textures plus a flattened draw list with
/// engine-space world transforms. Owns all meshes and textures; dispose to release them.
/// <br/>External textures stream their content in place: every texture object is final
/// when the scene is created, its content uploads asynchronously into the same native
/// texture, so material bindings never change after load.
/// </summary>
public sealed class ModelScene : AutoDisposable
{
    private readonly PrimitiveMesh[] _meshes;
    private readonly List<Texture2D> _textures;

    /// <summary>The materials; draw items index into this list.</summary>
    public IReadOnlyList<ModelMaterial> Materials { get; }

    /// <summary>The flattened renderable instances.</summary>
    public IReadOnlyList<ModelDrawItem> DrawItems { get; }

    /// <summary>Scene-space bounds of all draw items (minimum corner).</summary>
    public Vector3 BoundsMin { get; }

    /// <summary>Scene-space bounds of all draw items (maximum corner).</summary>
    public Vector3 BoundsMax { get; }

    /// <summary>
    /// Create a model scene taking ownership of the given meshes. Textures realized by
    /// the loader are taken over via <see cref="TakeOwnedTexture"/>.
    /// </summary>
    public ModelScene(
        IReadOnlyList<ModelMaterial> materials,
        IReadOnlyList<ModelDrawItem> drawItems,
        PrimitiveMesh[] meshes,
        in Vector3 boundsMin,
        in Vector3 boundsMax)
    {
        Materials = materials;
        DrawItems = drawItems;
        _meshes = meshes;
        _textures = new List<Texture2D>();
        BoundsMin = boundsMin;
        BoundsMax = boundsMax;
    }

    /// <summary>Take ownership of a texture the loader realized for this scene.</summary>
    internal void TakeOwnedTexture(Texture2D texture)
    {
        _textures.Add(texture);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        foreach (PrimitiveMesh mesh in _meshes)
        {
            mesh.Dispose();
        }
        foreach (Texture2D texture in _textures)
        {
            texture.Dispose();
        }
    }
}
