using Alco.IO;
using Alco.Rendering;
using Alco.Rendering.Utils;

namespace Alco.Engine;

/// <summary>
/// Asset loader for Wavefront OBJ model files.
/// Creates a <see cref="Model3D"/> with mesh and material data.
/// </summary>
public class AssetLoaderModel3DObj : BaseAssetLoader<Model3D>
{
    private readonly RenderingSystem _renderingSystem;
    private readonly Shader _defaultShader;
    private readonly ObjParser _parser = new();

    /// <inheritdoc/>
    public override string Name => "AssetLoader.Model3D.OBJ";

    /// <inheritdoc/>
    public override IReadOnlyList<string> FileExtensions { get; } = [FileExt.ModelOBJ];

    /// <summary>
    /// Initializes a new instance of the <see cref="AssetLoaderModel3DObj"/> class.
    /// </summary>
    /// <param name="renderingSystem">The rendering system used to create meshes and materials.</param>
    /// <param name="defaultShader">The default shader used for the material when no MTL file is available.</param>
    public AssetLoaderModel3DObj(RenderingSystem renderingSystem, Shader defaultShader)
    {
        _renderingSystem = renderingSystem ?? throw new ArgumentNullException(nameof(renderingSystem));
        _defaultShader = defaultShader ?? throw new ArgumentNullException(nameof(defaultShader));
    }

    /// <inheritdoc/>
    public override object CreateAsset(in AssetLoadContext context)
    {
        var result = _parser.Parse(context.Data);

        if (result.Vertices.Length == 0 || result.Indices.Length == 0)
            throw new InvalidOperationException($"OBJ file '{context.Filename}' contains no valid geometry.");

        var mesh = _renderingSystem.CreatePrimitiveMesh(
            result.Vertices,
            result.Indices,
            context.Filename);

        var material = _renderingSystem.CreateMaterial(_defaultShader, $"{context.Filename}_material");

        return new Model3D(mesh, material);
    }
}
