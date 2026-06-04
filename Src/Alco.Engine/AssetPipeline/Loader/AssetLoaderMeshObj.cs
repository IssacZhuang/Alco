using System.Runtime.InteropServices;
using Alco.IO;
using Alco.Rendering;

namespace Alco.Engine;

/// <summary>
/// Asset loader for Wavefront OBJ model files.
/// Creates a <see cref="Mesh"/> from the OBJ geometry data.
/// </summary>
public unsafe class AssetLoaderMeshObj : BaseAssetLoader<Mesh>
{
    private readonly RenderingSystem _renderingSystem;

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
        _renderingSystem = renderingSystem ?? throw new ArgumentNullException(nameof(renderingSystem));
    }

    /// <inheritdoc/>
    public override object CreateAsset(in AssetLoadContext context)
    {
        var data = context.GetData();

        VertexPositionNormalTexture* vertices = null;
        uint* indices = null;

        try
        {
            vertices = MeshDecodeUtility.DecodeObj(data, out int vertexCount, out indices, out int indexCount);

            if (vertexCount == 0 || indexCount == 0)
                throw new InvalidOperationException($"OBJ file '{context.Filename}' contains no valid geometry.");

            var vertexSpan = new ReadOnlySpan<VertexPositionNormalTexture>(vertices, vertexCount);
            var indexSpan = new ReadOnlySpan<uint>(indices, indexCount);

            return _renderingSystem.CreatePrimitiveMesh(vertexSpan, indexSpan, context.Filename);
        }
        finally
        {
            if (vertices != null)
                NativeMemory.Free(vertices);
            if (indices != null)
                NativeMemory.Free(indices);
        }
    }
}
