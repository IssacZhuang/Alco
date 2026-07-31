using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Alco.Rendering;

/// <summary>
/// Decoder for glTF 2.0 scenes (.gltf JSON and .glb binary container).
/// <br/>Produces GPU-ready <see cref="VertexPositionNormalTextureTangent"/> vertices and uint indices
/// in native memory, plus materials, images and flattened draw items.
/// <br/>Supported subset: float32 POSITION/NORMAL/TEXCOORD_0/TANGENT attributes, u8/u16/u32 indices,
/// TRIANGLES mode, TRS or matrix node transforms, metallic-roughness materials with
/// base color, normal and metallic-roughness textures. Sparse accessors, normalized integer
/// attributes, quantization and Draco compression are not supported.
/// <br/>When a primitive lacks a TANGENT attribute, tangents are computed from the triangle
/// UVs (area-weighted accumulation, orthogonalized against the normal, bitangent sign from
/// the UV handedness). Without TEXCOORD_0 a default orthogonal tangent is assigned.
/// <br/>Coordinates are converted from glTF's right-handed +Y-up to the engine's left-handed
/// +Z-up convention (x,y,z) → (-z,x,y); triangle winding and tangent signs are adjusted
/// accordingly (the conversion is a reflection).
/// </summary>
internal static unsafe class GltfDecoder
{
    /// <summary>The coordinate conversion matrix; node world matrices are conjugated with it.</summary>
    private static readonly Matrix4x4 Conversion = new(
        0, 0, -1, 0,
        1, 0, 0, 0,
        0, 1, 0, 0,
        0, 0, 0, 1);

    private static readonly Matrix4x4 ConversionInverse = Matrix4x4.Transpose(Conversion);

    /// <summary>
    /// Decode glTF/GLB data into a <see cref="GltfModel"/>.
    /// </summary>
    /// <param name="data">Raw .gltf (JSON) or .glb (binary container) file bytes.</param>
    /// <param name="resolver">Resolver for external buffer URIs (e.g. .bin files); may be null when all buffers are embedded.</param>
    /// <returns>The decoded model. Dispose to free native buffers.</returns>
    /// <exception cref="MeshDecodeException">Invalid or unsupported glTF data.</exception>
    public static GltfModel Decode(ReadOnlySpan<byte> data, GltfDecodeUtility.GltfBufferResolver? resolver)
    {
        if (data.Length >= 12 && data[0] == 'g' && data[1] == 'l' && data[2] == 'T' && data[3] == 'F')
        {
            return DecodeGlb(data, resolver);
        }

        using (JsonDocument document = JsonDocument.Parse(data.ToArray()))
        {
            var context = new ParseContext(resolver);
            try
            {
                context.LoadBuffers(document.RootElement, null, 0);
                return DecodeScene(document.RootElement, context);
            }
            finally
            {
                context.Dispose();
            }
        }
    }

    private static GltfModel DecodeGlb(ReadOnlySpan<byte> glb, GltfDecodeUtility.GltfBufferResolver? resolver)
    {
        // GLB container: 12-byte header (magic, version, total length), then length-prefixed chunks.
        uint version = MemoryMarshal.Read<uint>(glb[4..]);
        if (version != 2)
        {
            throw new MeshDecodeException($"Unsupported GLB version {version}, expected 2.");
        }

        ReadOnlySpan<byte> json = default;
        ReadOnlySpan<byte> bin = default;
        int offset = 12;
        while (offset + 8 <= glb.Length)
        {
            int chunkLength = MemoryMarshal.Read<int>(glb[offset..]);
            ReadOnlySpan<byte> chunkType = glb.Slice(offset + 4, 4);
            if (offset + 8 + chunkLength > glb.Length)
            {
                throw new MeshDecodeException("GLB chunk exceeds the file size.");
            }
            ReadOnlySpan<byte> chunkData = glb.Slice(offset + 8, chunkLength);
            if (chunkType.SequenceEqual("JSON"u8))
            {
                json = chunkData;
            }
            else if (chunkType.SequenceEqual("BIN\0"u8))
            {
                bin = chunkData;
            }
            // Chunks are padded to 4-byte alignment.
            offset += 8 + ((chunkLength + 3) & ~3);
        }

        if (json.IsEmpty)
        {
            throw new MeshDecodeException("GLB container has no JSON chunk.");
        }

        using (JsonDocument document = JsonDocument.Parse(json.ToArray()))
        {
            var context = new ParseContext(resolver);
            try
            {
                // The glb input bytes are owned by the caller and stay valid during decoding.
                byte* binPointer = bin.IsEmpty
                    ? null
                    : (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(bin));
                context.LoadBuffers(document.RootElement, binPointer, bin.Length);
                return DecodeScene(document.RootElement, context);
            }
            finally
            {
                context.Dispose();
            }
        }
    }

    private static GltfModel DecodeScene(JsonElement root, ParseContext context)
    {
        GltfImage[] images = ParseImages(root, context);
        GltfMaterial[] materials = ParseMaterials(root);

        var meshes = new List<GltfMesh>();
        var primitives = new List<GltfPrimitive>();
        if (root.TryGetProperty("meshes", out JsonElement meshesElement))
        {
            foreach (JsonElement meshElement in meshesElement.EnumerateArray())
            {
                ParseMesh(root, meshElement, context, meshes, primitives);
            }
        }

        var drawItems = new List<GltfDrawItem>();
        Vector3 boundsMin = Vector3.Zero;
        Vector3 boundsMax = Vector3.Zero;
        WalkSceneNodes(root, meshes, primitives, drawItems, ref boundsMin, ref boundsMax);

        return new GltfModel(
            [.. primitives],
            [.. meshes],
            materials,
            images,
            [.. drawItems],
            boundsMin,
            boundsMax);
    }

    // ---------- buffers ----------

    /// <summary>
    /// Holds resolved buffer pointers for the duration of one decode run.
    /// Spans returned by the resolver must stay valid until decoding finishes.
    /// </summary>
    private sealed class ParseContext : IDisposable
    {
        /// <summary>A raw buffer pointer with its length (pointer types cannot be tuple elements).</summary>
        private readonly unsafe struct BufferView
        {
            public readonly byte* Pointer;
            public readonly int Length;

            public BufferView(byte* pointer, int length)
            {
                Pointer = pointer;
                Length = length;
            }
        }

        private readonly GltfDecodeUtility.GltfBufferResolver? _resolver;
        private readonly List<BufferView> _buffers = new();
        private readonly List<GCHandle> _pins = new();
        private BufferView _glbBin;

        public ParseContext(GltfDecodeUtility.GltfBufferResolver? resolver)
        {
            _resolver = resolver;
        }

        public void LoadBuffers(JsonElement root, byte* glbBinPointer, int glbBinLength)
        {
            _glbBin = new BufferView(glbBinPointer, glbBinLength);

            if (!root.TryGetProperty("buffers", out JsonElement buffers))
            {
                return;
            }

            foreach (JsonElement buffer in buffers.EnumerateArray())
            {
                int byteLength = GetInt(buffer, "byteLength", 0);
                if (!buffer.TryGetProperty("uri", out JsonElement uriElement) || uriElement.ValueKind != JsonValueKind.String)
                {
                    if (_glbBin.Pointer == null)
                    {
                        throw new MeshDecodeException("glTF buffer has no URI and no GLB binary chunk is present.");
                    }
                    if (byteLength > _glbBin.Length)
                    {
                        throw new MeshDecodeException("glTF buffer exceeds the GLB binary chunk size.");
                    }
                    _buffers.Add(_glbBin);
                    continue;
                }

                string uri = Uri.UnescapeDataString(uriElement.GetString() ?? string.Empty);
                if (uri.StartsWith("data:", StringComparison.Ordinal))
                {
                    _buffers.Add(DecodeDataUri(uri, byteLength));
                    continue;
                }

                if (_resolver == null || !_resolver(uri, out ReadOnlySpan<byte> data))
                {
                    throw new MeshDecodeException($"Failed to resolve glTF buffer '{uri}'.");
                }
                if (data.Length < byteLength)
                {
                    throw new MeshDecodeException($"glTF buffer '{uri}' is {data.Length} bytes, expected {byteLength}.");
                }
                byte* pointer = data.IsEmpty
                    ? null
                    : (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(data));
                _buffers.Add(new BufferView(pointer, data.Length));
            }
        }

        public void GetBuffer(int index, out byte* pointer, out int length)
        {
            if ((uint)index >= (uint)_buffers.Count)
            {
                throw new MeshDecodeException($"glTF buffer index {index} out of range ({_buffers.Count} buffers).");
            }
            BufferView buffer = _buffers[index];
            pointer = buffer.Pointer;
            length = buffer.Length;
        }

        private BufferView DecodeDataUri(string uri, int expectedLength)
        {
            int comma = uri.IndexOf(',');
            if (comma < 0 || !uri.AsSpan(0, comma).EndsWith(";base64", StringComparison.Ordinal))
            {
                throw new MeshDecodeException("Only base64 data URIs are supported in glTF buffers.");
            }
            byte[] bytes = Convert.FromBase64String(uri[(comma + 1)..]);
            if (bytes.Length < expectedLength)
            {
                throw new MeshDecodeException($"Data URI buffer is {bytes.Length} bytes, expected {expectedLength}.");
            }
            GCHandle pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            _pins.Add(pin);
            return new BufferView((byte*)pin.AddrOfPinnedObject(), bytes.Length);
        }

        public void Dispose()
        {
            foreach (GCHandle pin in _pins)
            {
                pin.Free();
            }
            _pins.Clear();
            _buffers.Clear();
        }
    }

    // ---------- accessors ----------

    private readonly struct AccessorView
    {
        /// <summary>Pointer to the first element; null means the accessor is zero-filled.</summary>
        public readonly byte* Pointer;
        public readonly int Count;
        /// <summary>Bytes per element (buffer view stride, or element size when tightly packed).</summary>
        public readonly int Stride;
        public readonly int ComponentType;

        public AccessorView(byte* pointer, int count, int stride, int componentType)
        {
            Pointer = pointer;
            Count = count;
            Stride = stride;
            ComponentType = componentType;
        }
    }

    private static AccessorView GetAccessor(JsonElement root, ParseContext context, int index, string expectedType)
    {
        JsonElement accessor = root.GetProperty("accessors")[index];

        int count = GetInt(accessor, "count", 0);
        int componentType = GetInt(accessor, "componentType", 0);
        string type = GetString(accessor, "type", expectedType);
        int componentCount = type switch
        {
            "SCALAR" => 1,
            "VEC2" => 2,
            "VEC3" => 3,
            "VEC4" => 4,
            _ => throw new MeshDecodeException($"glTF accessor {index} has unsupported type '{type}'."),
        };
        if (type != expectedType)
        {
            throw new MeshDecodeException($"glTF accessor {index} has type {type}, expected {expectedType}.");
        }
        int componentSize = componentType switch
        {
            5120 or 5121 => 1,
            5122 or 5123 => 2,
            5125 or 5126 => 4,
            _ => throw new MeshDecodeException($"glTF accessor {index} has unsupported component type {componentType}."),
        };
        if (GetBool(accessor, "normalized", false))
        {
            throw new MeshDecodeException($"Normalized glTF accessor {index} is not supported.");
        }
        if (accessor.TryGetProperty("sparse", out _))
        {
            throw new MeshDecodeException($"Sparse glTF accessor {index} is not supported.");
        }

        int elementSize = componentSize * componentCount;
        if (!accessor.TryGetProperty("bufferView", out JsonElement bufferViewElement))
        {
            // An accessor without a buffer view is zero-filled by specification.
            return new AccessorView(null, count, elementSize, componentType);
        }

        JsonElement bufferView = root.GetProperty("bufferViews")[bufferViewElement.GetInt32()];
        context.GetBuffer(GetInt(bufferView, "buffer", 0), out byte* bufferPointer, out int bufferLength);
        int viewOffset = GetInt(bufferView, "byteOffset", 0);
        int viewLength = GetInt(bufferView, "byteLength", 0);
        int accessorOffset = GetInt(accessor, "byteOffset", 0);
        int stride = GetInt(bufferView, "byteStride", 0);
        if (stride == 0)
        {
            stride = elementSize;
        }
        if (stride < elementSize)
        {
            throw new MeshDecodeException($"glTF accessor {index} stride {stride} is smaller than its element size {elementSize}.");
        }
        if (viewOffset + viewLength > bufferLength)
        {
            throw new MeshDecodeException($"glTF buffer view of accessor {index} exceeds the buffer size.");
        }
        if (count > 0 && accessorOffset + (long)(count - 1) * stride + elementSize > viewLength)
        {
            throw new MeshDecodeException($"glTF accessor {index} exceeds its buffer view.");
        }

        return new AccessorView(bufferPointer + viewOffset + accessorOffset, count, stride, componentType);
    }

    private static Vector3 ReadFloat3(in AccessorView view, int index)
    {
        if (view.Pointer == null)
        {
            return Vector3.Zero;
        }
        float* p = (float*)(view.Pointer + (long)index * view.Stride);
        return new Vector3(p[0], p[1], p[2]);
    }

    private static Vector2 ReadFloat2(in AccessorView view, int index)
    {
        if (view.Pointer == null)
        {
            return Vector2.Zero;
        }
        float* p = (float*)(view.Pointer + (long)index * view.Stride);
        return new Vector2(p[0], p[1]);
    }

    private static Vector4 ReadFloat4(in AccessorView view, int index)
    {
        if (view.Pointer == null)
        {
            return Vector4.Zero;
        }
        float* p = (float*)(view.Pointer + (long)index * view.Stride);
        return new Vector4(p[0], p[1], p[2], p[3]);
    }

    private static uint ReadIndex(in AccessorView view, int index)
    {
        byte* p = view.Pointer + (long)index * view.Stride;
        return view.ComponentType switch
        {
            5121 => *p,
            5123 => *(ushort*)p,
            5125 => *(uint*)p,
            _ => throw new MeshDecodeException($"glTF index accessor has unsupported component type {view.ComponentType}."),
        };
    }

    /// <summary>Convert a glTF position/direction to engine space: right-handed +Y-up → left-handed +Z-up.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector3 ConvertVector(in Vector3 gltf) => new(-gltf.Z, gltf.X, gltf.Y);

    // ---------- meshes ----------

    private static void ParseMesh(
        JsonElement root,
        JsonElement meshElement,
        ParseContext context,
        List<GltfMesh> meshes,
        List<GltfPrimitive> primitives)
    {
        int primitiveStart = primitives.Count;

        foreach (JsonElement primitiveElement in meshElement.GetProperty("primitives").EnumerateArray())
        {
            primitives.Add(ParsePrimitive(root, primitiveElement, context));
        }

        meshes.Add(new GltfMesh
        {
            Name = GetString(meshElement, "name", string.Empty),
            PrimitiveStart = primitiveStart,
            PrimitiveCount = primitives.Count - primitiveStart,
        });
    }

    private static GltfPrimitive ParsePrimitive(JsonElement root, JsonElement primitiveElement, ParseContext context)
    {
        int mode = GetInt(primitiveElement, "mode", 4);
        if (mode != 4)
        {
            throw new MeshDecodeException($"Only TRIANGLES glTF primitives are supported, got mode {mode}.");
        }

        JsonElement attributes = primitiveElement.GetProperty("attributes");
        if (!attributes.TryGetProperty("POSITION", out JsonElement positionElement))
        {
            throw new MeshDecodeException("glTF primitive has no POSITION attribute.");
        }

        AccessorView positions = GetAccessor(root, context, positionElement.GetInt32(), "VEC3");
        if (positions.ComponentType != 5126)
        {
            throw new MeshDecodeException($"glTF POSITION accessor has component type {positions.ComponentType}, only float32 is supported.");
        }
        AccessorView normals = default;
        if (attributes.TryGetProperty("NORMAL", out JsonElement normalElement))
        {
            normals = GetAccessor(root, context, normalElement.GetInt32(), "VEC3");
            if (normals.ComponentType != 5126)
            {
                throw new MeshDecodeException("glTF NORMAL accessor must be float32.");
            }
        }
        AccessorView uvs = default;
        if (attributes.TryGetProperty("TEXCOORD_0", out JsonElement uvElement))
        {
            uvs = GetAccessor(root, context, uvElement.GetInt32(), "VEC2");
            if (uvs.ComponentType != 5126)
            {
                throw new MeshDecodeException("glTF TEXCOORD_0 accessor must be float32.");
            }
        }
        AccessorView tangents = default;
        if (attributes.TryGetProperty("TANGENT", out JsonElement tangentElement))
        {
            tangents = GetAccessor(root, context, tangentElement.GetInt32(), "VEC4");
            if (tangents.ComponentType != 5126)
            {
                throw new MeshDecodeException("glTF TANGENT accessor must be float32.");
            }
        }

        int vertexCount = positions.Count;
        if (normals.Pointer != null && normals.Count != vertexCount)
        {
            throw new MeshDecodeException("glTF NORMAL accessor count does not match POSITION.");
        }
        if (uvs.Pointer != null && uvs.Count != vertexCount)
        {
            throw new MeshDecodeException("glTF TEXCOORD_0 accessor count does not match POSITION.");
        }
        if (tangents.Pointer != null && tangents.Count != vertexCount)
        {
            throw new MeshDecodeException("glTF TANGENT accessor count does not match POSITION.");
        }

        var vertices = (VertexPositionNormalTextureTangent*)NativeMemory.AllocZeroed((nuint)(vertexCount * sizeof(VertexPositionNormalTextureTangent)));
        try
        {
            Vector3 boundsMin = new(float.MaxValue);
            Vector3 boundsMax = new(float.MinValue);
            for (int i = 0; i < vertexCount; i++)
            {
                Vector3 position = ConvertVector(ReadFloat3(positions, i));
                vertices[i].Position = position;
                vertices[i].Normal = normals.Pointer != null ? ConvertVector(ReadFloat3(normals, i)) : Vector3.UnitZ;
                vertices[i].UV = uvs.Pointer != null ? ReadFloat2(uvs, i) : Vector2.Zero;
                if (tangents.Pointer != null)
                {
                    Vector4 tangent = ReadFloat4(tangents, i);
                    // The coordinate conversion is a reflection, so the cross product
                    // it produces is negated: flip the bitangent sign to compensate.
                    vertices[i].Tangent = new Vector4(ConvertVector(new Vector3(tangent.X, tangent.Y, tangent.Z)), -tangent.W);
                }
                boundsMin = Vector3.Min(boundsMin, position);
                boundsMax = Vector3.Max(boundsMax, position);
            }
            if (vertexCount == 0)
            {
                boundsMin = Vector3.Zero;
                boundsMax = Vector3.Zero;
            }

            int indexCount;
            uint* indices;
            if (primitiveElement.TryGetProperty("indices", out JsonElement indicesElement))
            {
                AccessorView indexView = GetAccessor(root, context, indicesElement.GetInt32(), "SCALAR");
                indexCount = indexView.Count;
                if (indexCount % 3 != 0)
                {
                    throw new MeshDecodeException($"glTF index count {indexCount} is not a multiple of 3.");
                }
                indices = (uint*)NativeMemory.Alloc((nuint)(indexCount * sizeof(uint)));
                // The coordinate conversion flips handedness; swap two indices per
                // triangle to keep the same side front-facing.
                for (int t = 0; t < indexCount / 3; t++)
                {
                    indices[t * 3] = ReadIndex(indexView, t * 3);
                    indices[t * 3 + 1] = ReadIndex(indexView, t * 3 + 2);
                    indices[t * 3 + 2] = ReadIndex(indexView, t * 3 + 1);
                }
            }
            else
            {
                indexCount = vertexCount;
                indices = (uint*)NativeMemory.Alloc((nuint)(indexCount * sizeof(uint)));
                for (int t = 0; t < indexCount / 3; t++)
                {
                    indices[t * 3] = (uint)(t * 3);
                    indices[t * 3 + 1] = (uint)(t * 3 + 2);
                    indices[t * 3 + 2] = (uint)(t * 3 + 1);
                }
            }

            try
            {
                if (normals.Pointer == null && vertexCount > 0)
                {
                    GenerateNormals(vertices, vertexCount, indices, indexCount);
                }
                // Tangents need the final normals, so they come after normal generation.
                if (tangents.Pointer == null && vertexCount > 0)
                {
                    if (uvs.Pointer != null)
                    {
                        ComputeTangents(vertices, vertexCount, indices, indexCount);
                    }
                    else
                    {
                        SetDefaultTangents(vertices, vertexCount);
                    }
                }

                return new GltfPrimitive
                {
                    MaterialIndex = GetInt(primitiveElement, "material", -1),
                    VertexCount = vertexCount,
                    IndexCount = indexCount,
                    BoundsMin = boundsMin,
                    BoundsMax = boundsMax,
                    Vertices = vertices,
                    Indices = indices,
                };
            }
            catch
            {
                NativeMemory.Free(indices);
                throw;
            }
        }
        catch
        {
            NativeMemory.Free(vertices);
            throw;
        }
    }

    /// <summary>
    /// Generate smooth area-weighted normals for primitives that lack a NORMAL attribute.
    /// </summary>
    private static void GenerateNormals(VertexPositionNormalTextureTangent* vertices, int vertexCount, uint* indices, int indexCount)
    {
        for (int t = 0; t < indexCount / 3; t++)
        {
            uint i0 = indices[t * 3];
            uint i1 = indices[t * 3 + 1];
            uint i2 = indices[t * 3 + 2];
            Vector3 normal = Vector3.Cross(
                vertices[i1].Position - vertices[i0].Position,
                vertices[i2].Position - vertices[i0].Position);
            vertices[i0].Normal += normal;
            vertices[i1].Normal += normal;
            vertices[i2].Normal += normal;
        }
        for (int i = 0; i < vertexCount; i++)
        {
            Vector3 normal = vertices[i].Normal;
            vertices[i].Normal = normal.LengthSquared() > 1e-12f ? Vector3.Normalize(normal) : Vector3.UnitZ;
        }
    }

    /// <summary>
    /// Compute per-vertex tangents from triangle UVs (Lengyel's method): tangents and
    /// bitangents accumulate per triangle, then each tangent is orthogonalized against
    /// the normal and gets its bitangent sign from the accumulated UV handedness.
    /// </summary>
    private static void ComputeTangents(VertexPositionNormalTextureTangent* vertices, int vertexCount, uint* indices, int indexCount)
    {
        var bitangents = (Vector3*)NativeMemory.AllocZeroed((nuint)(vertexCount * sizeof(Vector3)));
        try
        {
            for (int t = 0; t < indexCount / 3; t++)
            {
                uint i0 = indices[t * 3];
                uint i1 = indices[t * 3 + 1];
                uint i2 = indices[t * 3 + 2];

                Vector3 edge1 = vertices[i1].Position - vertices[i0].Position;
                Vector3 edge2 = vertices[i2].Position - vertices[i0].Position;
                Vector2 duv1 = vertices[i1].UV - vertices[i0].UV;
                Vector2 duv2 = vertices[i2].UV - vertices[i0].UV;

                float det = duv1.X * duv2.Y - duv2.X * duv1.Y;
                if (MathF.Abs(det) < 1e-20f)
                {
                    continue;
                }
                float f = 1.0f / det;
                Vector3 tangent = (duv2.Y * edge1 - duv1.Y * edge2) * f;
                Vector3 bitangent = (duv1.X * edge2 - duv2.X * edge1) * f;

                AddTangent(vertices, i0, tangent);
                AddTangent(vertices, i1, tangent);
                AddTangent(vertices, i2, tangent);
                bitangents[i0] += bitangent;
                bitangents[i1] += bitangent;
                bitangents[i2] += bitangent;
            }

            for (int i = 0; i < vertexCount; i++)
            {
                Vector3 normal = vertices[i].Normal;
                Vector3 accumulated = new(vertices[i].Tangent.X, vertices[i].Tangent.Y, vertices[i].Tangent.Z);

                // Gram-Schmidt orthogonalization; fall back to an arbitrary orthogonal
                // when no usable tangent accumulated (degenerate UVs everywhere).
                Vector3 tangent = accumulated - normal * Vector3.Dot(normal, accumulated);
                if (tangent.LengthSquared() > 1e-12f)
                {
                    tangent = Vector3.Normalize(tangent);
                }
                else
                {
                    tangent = ArbitraryOrthogonal(normal);
                }

                float sign = Vector3.Dot(Vector3.Cross(normal, tangent), bitangents[i]) < 0.0f ? -1.0f : 1.0f;
                vertices[i].Tangent = new Vector4(tangent, sign);
            }
        }
        finally
        {
            NativeMemory.Free(bitangents);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AddTangent(VertexPositionNormalTextureTangent* vertices, uint index, Vector3 tangent)
    {
        ref Vector4 target = ref vertices[index].Tangent;
        target = new Vector4(target.X + tangent.X, target.Y + tangent.Y, target.Z + tangent.Z, 0.0f);
    }

    /// <summary>
    /// Assign an arbitrary orthogonal tangent to every vertex (no UVs to derive one from).
    /// </summary>
    private static void SetDefaultTangents(VertexPositionNormalTextureTangent* vertices, int vertexCount)
    {
        for (int i = 0; i < vertexCount; i++)
        {
            vertices[i].Tangent = new Vector4(ArbitraryOrthogonal(vertices[i].Normal), 1.0f);
        }
    }

    private static Vector3 ArbitraryOrthogonal(in Vector3 normal)
    {
        Vector3 axis = MathF.Abs(normal.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX;
        Vector3 orthogonal = Vector3.Cross(normal, axis);
        return orthogonal.LengthSquared() > 1e-12f ? Vector3.Normalize(orthogonal) : Vector3.UnitX;
    }

    // ---------- materials and images ----------

    private static GltfImage[] ParseImages(JsonElement root, ParseContext context)
    {
        if (!root.TryGetProperty("images", out JsonElement imagesElement))
        {
            return [];
        }

        var images = new List<GltfImage>();
        int index = 0;
        foreach (JsonElement imageElement in imagesElement.EnumerateArray())
        {
            string name = GetString(imageElement, "name", $"image_{index}");
            if (imageElement.TryGetProperty("uri", out JsonElement uriElement) && uriElement.ValueKind == JsonValueKind.String)
            {
                string uri = Uri.UnescapeDataString(uriElement.GetString() ?? string.Empty);
                if (uri.StartsWith("data:", StringComparison.Ordinal))
                {
                    images.Add(DecodeDataUriImage(uri, name));
                }
                else
                {
                    images.Add(new GltfImage { Name = name, Uri = uri });
                }
            }
            else if (imageElement.TryGetProperty("bufferView", out JsonElement bufferViewElement))
            {
                JsonElement bufferView = root.GetProperty("bufferViews")[bufferViewElement.GetInt32()];
                context.GetBuffer(GetInt(bufferView, "buffer", 0), out byte* bufferPointer, out int bufferLength);
                int viewOffset = GetInt(bufferView, "byteOffset", 0);
                int viewLength = GetInt(bufferView, "byteLength", 0);
                if (viewOffset + viewLength > bufferLength)
                {
                    throw new MeshDecodeException($"glTF image {index} buffer view exceeds the buffer size.");
                }
                var data = new byte[viewLength];
                new ReadOnlySpan<byte>(bufferPointer + viewOffset, viewLength).CopyTo(data);
                images.Add(new GltfImage(data)
                {
                    Name = name,
                    MimeType = GetString(imageElement, "mimeType", "image/png"),
                });
            }
            else
            {
                images.Add(new GltfImage { Name = name });
            }
            index++;
        }
        return [.. images];
    }

    private static GltfImage DecodeDataUriImage(string uri, string name)
    {
        int comma = uri.IndexOf(',');
        if (comma < 0)
        {
            throw new MeshDecodeException($"Invalid data URI in glTF image '{name}'.");
        }
        string header = uri[5..comma]; // skip "data:"
        if (!header.EndsWith(";base64", StringComparison.Ordinal))
        {
            throw new MeshDecodeException("Only base64 data URIs are supported in glTF images.");
        }
        byte[] bytes = Convert.FromBase64String(uri[(comma + 1)..]);
        string mimeType = header[..^7]; // strip ";base64"
        return new GltfImage(bytes)
        {
            Name = name,
            MimeType = string.IsNullOrEmpty(mimeType) ? "image/png" : mimeType,
        };
    }

    private static GltfMaterial[] ParseMaterials(JsonElement root)
    {
        if (!root.TryGetProperty("materials", out JsonElement materialsElement))
        {
            return [];
        }

        var materials = new List<GltfMaterial>();
        foreach (JsonElement materialElement in materialsElement.EnumerateArray())
        {
            JsonElement pbr = default;
            bool hasPbr = materialElement.TryGetProperty("pbrMetallicRoughness", out pbr);

            Vector4 baseColorFactor = Vector4.One;
            float metallicFactor = 1.0f;
            float roughnessFactor = 1.0f;
            int baseColorImageIndex = -1;
            int normalImageIndex = -1;
            int metallicRoughnessImageIndex = -1;
            Graphics.AddressMode wrapS = Graphics.AddressMode.Repeat;
            Graphics.AddressMode wrapT = Graphics.AddressMode.Repeat;
            Graphics.AddressMode normalWrapS = Graphics.AddressMode.Repeat;
            Graphics.AddressMode metallicRoughnessWrapS = Graphics.AddressMode.Repeat;

            if (hasPbr)
            {
                if (pbr.TryGetProperty("baseColorFactor", out JsonElement baseColorElement))
                {
                    baseColorFactor = ReadVector4(baseColorElement);
                }
                // FBX-derived glTF exports ship a metallic-roughness texture for every
                // material and leave metallicFactor at the spec default 1, which would
                // turn everything chrome when the texture is ignored. Treat an implicit
                // factor as dielectric in that case.
                bool hasExplicitMetallic = pbr.TryGetProperty("metallicFactor", out JsonElement metallicElement);
                bool hasMrTexture = pbr.TryGetProperty("metallicRoughnessTexture", out JsonElement mrTextureElement);
                metallicFactor = hasExplicitMetallic ? metallicElement.GetSingle() : (hasMrTexture ? 0.0f : 1.0f);
                roughnessFactor = GetFloat(pbr, "roughnessFactor", 1.0f);

                if (hasMrTexture)
                {
                    metallicRoughnessImageIndex = ResolveTextureImage(root, mrTextureElement, out metallicRoughnessWrapS, out _);
                }
                if (pbr.TryGetProperty("baseColorTexture", out JsonElement baseColorTexture))
                {
                    baseColorImageIndex = ResolveTextureImage(root, baseColorTexture, out wrapS, out wrapT);
                }
            }

            if (materialElement.TryGetProperty("normalTexture", out JsonElement normalTexture))
            {
                normalImageIndex = ResolveTextureImage(root, normalTexture, out normalWrapS, out _);
            }

            GltfAlphaMode alphaMode = GetString(materialElement, "alphaMode", "OPAQUE") switch
            {
                "MASK" => GltfAlphaMode.Mask,
                "BLEND" => GltfAlphaMode.Blend,
                _ => GltfAlphaMode.Opaque,
            };

            materials.Add(new GltfMaterial
            {
                Name = GetString(materialElement, "name", string.Empty),
                BaseColorFactor = baseColorFactor,
                MetallicFactor = metallicFactor,
                RoughnessFactor = roughnessFactor,
                BaseColorImageIndex = baseColorImageIndex,
                NormalImageIndex = normalImageIndex,
                MetallicRoughnessImageIndex = metallicRoughnessImageIndex,
                WrapS = wrapS,
                WrapT = wrapT,
                NormalWrapS = normalWrapS,
                MetallicRoughnessWrapS = metallicRoughnessWrapS,
                AlphaMode = alphaMode,
                AlphaCutoff = GetFloat(materialElement, "alphaCutoff", 0.5f),
                DoubleSided = GetBool(materialElement, "doubleSided", false),
            });
        }
        return [.. materials];
    }

    /// <summary>
    /// Resolve a glTF texture reference (a textureInfo object) to an image index plus sampler wrap modes.
    /// </summary>
    private static int ResolveTextureImage(JsonElement root, JsonElement textureInfo, out Graphics.AddressMode wrapS, out Graphics.AddressMode wrapT)
    {
        wrapS = Graphics.AddressMode.Repeat;
        wrapT = Graphics.AddressMode.Repeat;

        int textureIndex = GetInt(textureInfo, "index", -1);
        if (textureIndex < 0 || !root.TryGetProperty("textures", out JsonElement texturesElement))
        {
            return -1;
        }

        JsonElement texture = texturesElement[textureIndex];
        if (texture.TryGetProperty("sampler", out JsonElement samplerElement) && root.TryGetProperty("samplers", out JsonElement samplersElement))
        {
            JsonElement sampler = samplersElement[samplerElement.GetInt32()];
            wrapS = ConvertWrapMode(GetInt(sampler, "wrapS", 10497));
            wrapT = ConvertWrapMode(GetInt(sampler, "wrapT", 10497));
        }

        return GetInt(texture, "source", -1);
    }

    private static Graphics.AddressMode ConvertWrapMode(int gltfWrap) => gltfWrap switch
    {
        33071 => Graphics.AddressMode.ClampToEdge,
        33648 => Graphics.AddressMode.MirrorRepeat,
        _ => Graphics.AddressMode.Repeat,
    };

    // ---------- scene graph ----------

    private static void WalkSceneNodes(
        JsonElement root,
        List<GltfMesh> meshes,
        List<GltfPrimitive> primitives,
        List<GltfDrawItem> drawItems,
        ref Vector3 boundsMin,
        ref Vector3 boundsMax)
    {
        if (!root.TryGetProperty("nodes", out JsonElement nodesElement))
        {
            return;
        }

        int sceneIndex = GetInt(root, "scene", 0);
        if (!root.TryGetProperty("scenes", out JsonElement scenesElement) || sceneIndex >= scenesElement.GetArrayLength())
        {
            return;
        }
        if (!scenesElement[sceneIndex].TryGetProperty("nodes", out JsonElement rootNodes))
        {
            return;
        }

        bool first = true;
        foreach (JsonElement nodeIndexElement in rootNodes.EnumerateArray())
        {
            WalkNode(nodesElement, nodeIndexElement.GetInt32(), Matrix4x4.Identity, meshes, primitives, drawItems, ref boundsMin, ref boundsMax, ref first);
        }
    }

    private static void WalkNode(
        JsonElement nodesElement,
        int nodeIndex,
        in Matrix4x4 parentWorld,
        List<GltfMesh> meshes,
        List<GltfPrimitive> primitives,
        List<GltfDrawItem> drawItems,
        ref Vector3 boundsMin,
        ref Vector3 boundsMax,
        ref bool first)
    {
        JsonElement node = nodesElement[nodeIndex];
        Matrix4x4 local = GetNodeLocalTransform(node);
        // System.Numerics row-vector convention: child world = local * parent.
        Matrix4x4 world = local * parentWorld;

        if (node.TryGetProperty("mesh", out JsonElement meshElement))
        {
            int meshIndex = meshElement.GetInt32();
            Matrix4x4 engineWorld = Conversion * world * ConversionInverse;
            drawItems.Add(new GltfDrawItem(meshIndex, engineWorld));

            GltfMesh mesh = meshes[meshIndex];
            for (int i = mesh.PrimitiveStart; i < mesh.PrimitiveStart + mesh.PrimitiveCount; i++)
            {
                ExpandBounds(primitives[i].BoundsMin, primitives[i].BoundsMax, engineWorld, ref boundsMin, ref boundsMax, ref first);
            }
        }

        if (node.TryGetProperty("children", out JsonElement children))
        {
            foreach (JsonElement child in children.EnumerateArray())
            {
                WalkNode(nodesElement, child.GetInt32(), world, meshes, primitives, drawItems, ref boundsMin, ref boundsMax, ref first);
            }
        }
    }

    private static Matrix4x4 GetNodeLocalTransform(JsonElement node)
    {
        if (node.TryGetProperty("matrix", out JsonElement matrixElement))
        {
            // glTF matrices are column-major; transpose into the engine's row-major layout.
            Span<float> m = stackalloc float[16];
            int i = 0;
            foreach (JsonElement value in matrixElement.EnumerateArray())
            {
                m[i++] = value.GetSingle();
            }
            return new Matrix4x4(
                m[0], m[4], m[8], m[12],
                m[1], m[5], m[9], m[13],
                m[2], m[6], m[10], m[14],
                m[3], m[7], m[11], m[15]);
        }

        Vector3 translation = Vector3.Zero;
        Quaternion rotation = Quaternion.Identity;
        Vector3 scale = Vector3.One;
        if (node.TryGetProperty("translation", out JsonElement translationElement))
        {
            translation = ReadVector3(translationElement);
        }
        if (node.TryGetProperty("rotation", out JsonElement rotationElement))
        {
            rotation = new Quaternion(
                rotationElement[0].GetSingle(),
                rotationElement[1].GetSingle(),
                rotationElement[2].GetSingle(),
                rotationElement[3].GetSingle());
        }
        if (node.TryGetProperty("scale", out JsonElement scaleElement))
        {
            scale = ReadVector3(scaleElement);
        }
        return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(translation);
    }

    private static void ExpandBounds(
        in Vector3 localMin,
        in Vector3 localMax,
        in Matrix4x4 world,
        ref Vector3 boundsMin,
        ref Vector3 boundsMax,
        ref bool first)
    {
        for (int corner = 0; corner < 8; corner++)
        {
            Vector3 point = new(
                (corner & 1) == 0 ? localMin.X : localMax.X,
                (corner & 2) == 0 ? localMin.Y : localMax.Y,
                (corner & 4) == 0 ? localMin.Z : localMax.Z);
            Vector3 transformed = Vector3.Transform(point, world);
            if (first)
            {
                boundsMin = transformed;
                boundsMax = transformed;
                first = false;
            }
            else
            {
                boundsMin = Vector3.Min(boundsMin, transformed);
                boundsMax = Vector3.Max(boundsMax, transformed);
            }
        }
    }

    // ---------- JSON helpers ----------

    private static int GetInt(JsonElement obj, string name, int defaultValue)
        => obj.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : defaultValue;

    private static float GetFloat(JsonElement obj, string name, float defaultValue)
        => obj.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number
            ? value.GetSingle()
            : defaultValue;

    private static bool GetBool(JsonElement obj, string name, bool defaultValue)
    {
        if (!obj.TryGetProperty(name, out JsonElement value))
        {
            return defaultValue;
        }
        return value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : defaultValue;
    }

    private static string GetString(JsonElement obj, string name, string defaultValue)
        => obj.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? defaultValue
            : defaultValue;

    private static Vector3 ReadVector3(JsonElement array)
        => new(array[0].GetSingle(), array[1].GetSingle(), array[2].GetSingle());

    private static Vector4 ReadVector4(JsonElement array)
        => new(array[0].GetSingle(), array[1].GetSingle(), array[2].GetSingle(), array[3].GetSingle());
}
