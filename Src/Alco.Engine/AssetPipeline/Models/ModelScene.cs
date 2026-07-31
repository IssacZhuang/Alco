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
    /// is still streaming in (the pipeline falls back to white until assigned).
    /// </summary>
    public Texture2D? AlbedoTexture { get; set; }

    /// <summary>
    /// The normal map texture (tangent space), null when the material has none or the
    /// texture is still streaming in (the pipeline falls back to a flat normal until assigned).
    /// </summary>
    public Texture2D? NormalTexture { get; set; }

    /// <summary>
    /// The metallic-roughness texture (roughness in G, metallic in B), null when the
    /// material has none or the texture is still streaming in (the pipeline falls back
    /// to white, i.e. the factors pass through, until assigned).
    /// </summary>
    public Texture2D? MetallicRoughnessTexture { get; set; }

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

    /// <summary>Create a draw item.</summary>
    public ModelDrawItem(PrimitiveMesh mesh, int materialIndex, in Matrix4x4 world)
    {
        Mesh = mesh;
        MaterialIndex = materialIndex;
        World = world;
    }
}

/// <summary>
/// A loaded 3D model scene: GPU meshes and textures plus a flattened draw list with
/// engine-space world transforms. Owns all meshes and textures; dispose to release them.
/// <br/>Textures may stream in asynchronously after creation: materials start with null
/// albedo/normal/metallic-roughness textures and get them assigned on the main thread
/// as loads complete.
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
    /// Completes when all asynchronously streaming textures have arrived (or failed).
    /// <see cref="Task.CompletedTask"/> when nothing streams.
    /// </summary>
    public Task LoadingCompletion { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Create a model scene taking ownership of the given meshes. Textures created later
    /// by asynchronous streaming are taken over via <see cref="TakeOwnedTexture"/>.
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

    /// <summary>Set the task that completes when all streaming textures have arrived.</summary>
    internal void SetLoadingCompletion(Task completion)
    {
        LoadingCompletion = completion;
    }

    /// <summary>
    /// Take ownership of a texture that finished streaming in. When the scene is already
    /// disposed the texture is disposed immediately instead and false is returned.
    /// </summary>
    internal bool TakeOwnedTexture(Texture2D texture)
    {
        if (IsDisposed)
        {
            texture.Dispose();
            return false;
        }
        _textures.Add(texture);
        return true;
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
