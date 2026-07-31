namespace Alco.Rendering;

/// <summary>
/// Static facade for mesh decoding. Dispatches to format-specific decoders.
/// All methods are thread-safe. Returned pointers are caller-owned and must be freed via <c>NativeMemory.Free</c>.
/// </summary>
public static unsafe class MeshDecodeUtility
{
    /// <summary>
    /// Decode OBJ data into vertex and index buffers.
    /// </summary>
    /// <param name="data">Raw OBJ file bytes.</param>
    /// <param name="vertexCount">Number of decoded vertices.</param>
    /// <param name="indices">Pointer to decoded index data. Caller must free via <c>NativeMemory.Free</c>.</param>
    /// <param name="indexCount">Number of decoded indices.</param>
    /// <returns>Pointer to vertex data. Caller must free via <c>NativeMemory.Free</c>.</returns>
    /// <exception cref="MeshDecodeException">Invalid or unsupported OBJ data.</exception>
    public static VertexPositionNormalTexture* DecodeObj(ReadOnlySpan<byte> data, out int vertexCount, out uint* indices, out int indexCount)
        => ObjDecoder.Decode(data, out vertexCount, out indices, out indexCount);

    /// <summary>
    /// Auto-detect mesh format and decode into vertex and index buffers.
    /// Currently only supports OBJ format.
    /// </summary>
    /// <param name="data">Raw mesh file bytes.</param>
    /// <param name="vertexCount">Number of decoded vertices.</param>
    /// <param name="indices">Pointer to decoded index data. Caller must free via <c>NativeMemory.Free</c>.</param>
    /// <param name="indexCount">Number of decoded indices.</param>
    /// <returns>Pointer to vertex data. Caller must free via <c>NativeMemory.Free</c>.</returns>
    /// <exception cref="MeshDecodeException">Unrecognized format or corrupt data.</exception>
    public static VertexPositionNormalTexture* DecodeAuto(ReadOnlySpan<byte> data, out int vertexCount, out uint* indices, out int indexCount)
    {
        // Currently only OBJ is supported; defer to OBJ decoder directly.
        return ObjDecoder.Decode(data, out vertexCount, out indices, out indexCount);
    }
}
