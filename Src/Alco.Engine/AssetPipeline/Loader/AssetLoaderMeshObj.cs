using Alco.IO;
using Alco.Rendering;
using Alco.Rendering.Utils;

namespace Alco.Engine;

/// <summary>
/// Asset loader for Wavefront OBJ mesh files.
/// </summary>
public class AssetLoaderMeshObj : BaseAssetLoader<PrimitiveMesh>
{
    private readonly RenderingSystem _renderingSystem;
    private readonly ObjParser _parser = new();

    /// <inheritdoc/>
    public override string Name => "AssetLoader.Mesh.OBJ";

    /// <inheritdoc/>
    public override IReadOnlyList<string> FileExtensions { get; } = [FileExt.ModelOBJ];

    /// <summary>
    /// Initializes a new instance of the <see cref="AssetLoaderMeshObj"/> class.
    /// </summary>
    /// <param name="renderingSystem">The rendering system used to create meshes.</param>
    public AssetLoaderMeshObj(RenderingSystem renderingSystem)
    {
        _renderingSystem = renderingSystem;
    }

    /// <inheritdoc/>
    public override object CreateAsset(in AssetLoadContext context)
    {
        var result = _parser.Parse(context.Data);

        if (result.Vertices.Length == 0 || result.Indices.Length == 0)
            throw new InvalidOperationException($"OBJ file '{context.Filename}' contains no valid geometry.");

        return _renderingSystem.CreatePrimitiveMesh(
            result.Vertices,
            result.Indices,
            context.Filename);
    }
}
