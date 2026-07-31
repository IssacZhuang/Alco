namespace Alco.Rendering;

/// <summary>
/// Static facade for glTF 2.0 scene decoding. Dispatches to <see cref="GltfDecoder"/>.
/// All methods are thread-safe. The returned model owns native buffers and must be disposed.
/// </summary>
public static class GltfDecodeUtility
{
    /// <summary>
    /// Resolves an external buffer URI (e.g. a .bin file referenced by a .gltf file).
    /// The returned span must remain valid until the decoding call returns.
    /// </summary>
    /// <param name="uri">The decoded (unescaped) buffer URI, relative to the glTF file.</param>
    /// <param name="data">The buffer bytes.</param>
    /// <returns>True when the buffer was resolved.</returns>
    public delegate bool GltfBufferResolver(string uri, out ReadOnlySpan<byte> data);

    /// <summary>
    /// Decode glTF/GLB data into a <see cref="GltfModel"/>. The container format is
    /// auto-detected from the GLB magic bytes.
    /// </summary>
    /// <param name="data">Raw .gltf (JSON) or .glb (binary container) file bytes.</param>
    /// <param name="resolver">Resolver for external buffer URIs; may be null when all buffers are embedded.</param>
    /// <returns>The decoded model. Dispose to free native buffers.</returns>
    /// <exception cref="MeshDecodeException">Invalid or unsupported glTF data.</exception>
    public static GltfModel DecodeAuto(ReadOnlySpan<byte> data, GltfBufferResolver? resolver = null)
        => GltfDecoder.Decode(data, resolver);

    /// <summary>
    /// Decode glTF JSON data into a <see cref="GltfModel"/>.
    /// </summary>
    /// <param name="data">Raw .gltf JSON file bytes.</param>
    /// <param name="resolver">Resolver for external buffer URIs; may be null when all buffers are embedded.</param>
    /// <returns>The decoded model. Dispose to free native buffers.</returns>
    /// <exception cref="MeshDecodeException">Invalid or unsupported glTF data.</exception>
    public static GltfModel DecodeGltf(ReadOnlySpan<byte> data, GltfBufferResolver? resolver = null)
        => GltfDecoder.Decode(data, resolver);
}
