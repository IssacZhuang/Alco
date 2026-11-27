using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text.Json;
using Alco.Graphics;
using Alco.Rendering;
using Alco.World3D;

/// <summary>Imports the static, indexed triangle subset used by the Bistro glTF files.</summary>
internal sealed partial class BistroImporter : IDisposable
{
    private const string ExteriorFile = "BistroExterior.gltf";
    private const string InteriorFile = "BistroInterior.gltf";
    private const string ExteriorNodePrefix = "Bistro_Research_Exterior_";
    private const int GeometryBatchBytes = 8 * 1024 * 1024;
    private const int DrawsPerBatch = 128;
    private const int MaxAccessorBytes = 16 * 1024 * 1024;
    private const int MaxDocumentBytes = 8 * 1024 * 1024;
    private const int Float = 5126;
    private const int UnsignedShort = 5123;
    private const int UnsignedInt = 5125;

    // Row-vector change of basis: (x, y, z) -> (-z, x, y).
    private static readonly Matrix4x4 Basis = new(
        0, 1, 0, 0, 0, 0, 1, 0, -1, 0, 0, 0, 0, 0, 0, 1);
    private static readonly Matrix4x4 InverseBasis = Matrix4x4.Transpose(Basis);
    private static readonly Matrix4x4 Reflection = Matrix4x4.CreateScale(-1, 1, 1);
    private static readonly string[] BistroFiles = [ExteriorFile, InteriorFile];

    private readonly RenderingSystem _system;
    private readonly World3DScene _scene;
    private readonly WorldSectorHandle _sector;
    private readonly Source _source;
    private int _nextTexture;
    private int _nextDraw;
    private bool _materialsRegistered;

    private BistroImporter(RenderingSystem system, World3DScene scene, WorldSectorHandle sector, Source source)
    {
        _system = system;
        _scene = scene;
        _sector = sector;
        _source = source;
    }

    /// <summary>Gets the two glTF files required in the Bistro directory.</summary>
    public static ReadOnlySpan<string> SceneFiles => BistroFiles;

    /// <summary>Gets whether every imported instance and geometry upload has been queued.</summary>
    public bool AllQueued => _nextDraw == _source.Draws.Count;

    /// <summary>Gets a summary of the registered content and upload payloads.</summary>
    public string Summary => $"exterior={_source.Draws.Count - _source.InteriorDraws}, interior={_source.InteriorDraws}, " +
        $"opaque={_source.Draws.Count - _source.Masked}, mask={_source.Masked}, skippedGlass={_source.SkippedGlass}, " +
        $"materials={_source.Materials.Count}, textures={_source.Textures.Count}, textureMiB={_source.TextureBytes / (1024.0 * 1024.0):F1}";

    /// <summary>Validates both local Bistro glTF files and plans their uploads into one World3D scene.</summary>
    public static bool TryOpen(RenderingSystem system, World3DScene scene, string directory, bool softwareBc,
        bool legacyMetallic, [NotNullWhen(true)] out BistroImporter? importer, [NotNullWhen(false)] out string? error)
    {
        importer = null;
        error = null;
        var buffers = new List<FileStream>();
        try
        {
            var textures = new TextureCatalog(directory) { SoftwareBc = softwareBc };
            ScenePlan exterior = ReadPart(directory, ExteriorFile, buffers, textures, legacyMetallic);
            ScenePlan interior = ReadPart(directory, InteriorFile, buffers, textures, legacyMetallic);
            Source source = new(directory, buffers, [.. exterior.Draws, .. interior.Draws],
                [.. exterior.Geometries, .. interior.Geometries], [.. exterior.Materials, .. interior.Materials],
                textures.Plans, textures.UploadBytes, exterior.Masked + interior.Masked,
                exterior.SkippedGlass + interior.SkippedGlass, interior.Draws.Count);
            if (!scene.TryAttachSector(out WorldSectorHandle sector))
            {
                error = "World3D could not attach the Bistro sector.";
                return false;
            }
            importer = new BistroImporter(system, scene, sector, source);
            return true;
        }
        catch (Exception ex) when (IsBadAsset(ex))
        {
            error = ex.Message;
            return false;
        }
        finally
        {
            if (importer == null)
            {
                foreach (FileStream buffer in buffers)
                    buffer.Dispose();
            }
        }
    }

    /// <summary>Uploads a bounded texture batch or queues one bounded geometry batch for the next scene frame.</summary>
    public bool TryQueueNextBatch(out bool recordFrame, [NotNullWhen(false)] out string? error)
    {
        recordFrame = false;
        error = null;
        try
        {
            if (_nextTexture < _source.Textures.Count)
            {
                UploadTextureBatch();
                _system.FlushUploads();
                return true;
            }
            if (!_materialsRegistered)
            {
                foreach (MaterialPlan material in _source.Materials)
                {
                    if (!_scene.TryRegisterMaterial(material.Description, out material.Handle))
                    {
                        error = $"World3D could not register Bistro material '{material.Name}'.";
                        return false;
                    }
                }
                _materialsRegistered = true;
            }

            ulong remaining = Math.Min(_scene.GeometryUploadBudgetBytes & ~3ul, GeometryBatchBytes);
            for (int count = 0; _nextDraw < _source.Draws.Count && count < DrawsPerBatch; count++, _nextDraw++)
            {
                DrawPlan draw = _source.Draws[_nextDraw];
                GeometryPlan geometry = draw.Geometry;
                if (geometry.Handle == default)
                {
                    if ((uint)geometry.UploadBytes > remaining)
                    {
                        if (count != 0)
                            break;
                        error = $"Bistro geometry '{geometry.Name}' exceeds the per-frame upload budget.";
                        return false;
                    }
                    if (!TryUploadGeometry(geometry))
                    {
                        error = $"World3D could not queue Bistro geometry '{geometry.Name}'.";
                        return false;
                    }
                    remaining -= (uint)geometry.UploadBytes;
                }
                if (!_scene.TryAddInstance(new World3DInstanceDesc(_sector, geometry.Handle, draw.Material.Handle, draw.Transform), out _))
                {
                    error = $"World3D could not add an instance of '{geometry.Name}'.";
                    return false;
                }
            }
            recordFrame = true;
            return true;
        }
        catch (Exception ex) when (IsBadAsset(ex))
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Checks that every draw was registered and all geometry became resident after submission.</summary>
    public bool IsResident() => AllQueued && _scene.InstanceCount == _source.Draws.Count &&
        _source.Geometries.All(geometry => _scene.IsGeometryLodResident(geometry.Handle, 0));

    /// <summary>Closes the importer’s CPU files; queued GPU resources are owned by the scene.</summary>
    public void Dispose()
    {
        foreach (FileStream buffer in _source.Buffers)
            buffer.Dispose();
    }

    private static ScenePlan ReadPart(string directory, string filename, List<FileStream> ownedBuffers, TextureCatalog textures, bool legacyMetallic)
    {
        try
        {
            using FileStream gltf = OpenAsset(directory, filename);
            if (gltf.Length > MaxDocumentBytes)
                throw new InvalidDataException("The glTF JSON document exceeds the Bistro import limit.");
            using JsonDocument document = JsonDocument.Parse(gltf);
            JsonElement root = document.RootElement;
            if (Text(root.GetProperty("asset"), "version", "") != "2.0" ||
                Property(root, "extensionsRequired") is { ValueKind: JsonValueKind.Array } required && required.GetArrayLength() != 0)
                throw new InvalidDataException("Expected glTF 2.0 with no required extensions.");

            JsonElement bufferJson = root.GetProperty("buffers");
            int bufferCount = bufferJson.GetArrayLength();
            if (bufferCount is < 1 or > 16)
                throw new InvalidDataException("Expected between 1 and 16 external glTF buffers.");
            var buffers = new FileStream[bufferCount];
            for (int i = 0; i < buffers.Length; i++)
            {
                JsonElement buffer = bufferJson[i];
                int byteLength = buffer.GetProperty("byteLength").GetInt32();
                string uri = Text(buffer, "uri", "");
                FileStream stream = OpenAsset(directory, uri);
                ownedBuffers.Add(stream);
                if (byteLength <= 0 || stream.Length != byteLength)
                    throw new InvalidDataException($"glTF buffer '{uri}' does not match its declared byteLength.");
                buffers[i] = stream;
            }
            return ReadScene(root, buffers, textures, legacyMetallic, filename == InteriorFile);
        }
        catch (Exception ex) when (IsBadAsset(ex))
        {
            throw new InvalidDataException($"{filename}: {ex.Message}", ex);
        }
    }

    private static ScenePlan ReadScene(JsonElement root, FileStream[] buffers, TextureCatalog textures, bool legacyMetallic, bool interior)
    {
        JsonElement nodes = root.GetProperty("nodes");
        JsonElement meshes = root.GetProperty("meshes");
        JsonElement scene = At(root.GetProperty("scenes"), Integer(root, "scene", 0));
        var visited = new HashSet<int>();
        var draws = new List<DrawPlan>();
        var geometries = new Dictionary<GeometryKey, GeometryPlan>();
        var materials = new MaterialReader(root, textures) { LegacyMetallic = legacyMetallic };
        int masked = 0;
        int skippedGlass = 0;
        foreach (JsonElement node in scene.GetProperty("nodes").EnumerateArray())
            Visit(node.GetInt32(), Matrix4x4.Identity, 0);
        if (draws.Count == 0)
            throw new InvalidDataException("The glTF scene contains no supported draw items.");
        return new ScenePlan(draws, [.. geometries.Values], materials.DrawMaterials, masked, skippedGlass);

        void Visit(int nodeIndex, Matrix4x4 parent, int depth)
        {
            if (depth >= 128 || !visited.Add(nodeIndex))
                throw new InvalidDataException($"Invalid glTF node hierarchy at node {nodeIndex}.");
            JsonElement node = At(nodes, nodeIndex);
            Matrix4x4 world = ReadLocalTransform(node) * parent;
            // The interior export repeats the exterior façade at the same world coordinates.
            bool duplicateFacade = interior && Text(node, "name", "").StartsWith(ExteriorNodePrefix, StringComparison.Ordinal);
            if (!duplicateFacade && node.TryGetProperty("mesh", out JsonElement meshIndexElement))
            {
                if (node.TryGetProperty("skin", out _) || node.TryGetProperty("weights", out _))
                    throw new InvalidDataException("Bistro only supports static, unskinned meshes.");
                int meshIndex = meshIndexElement.GetInt32();
                Matrix4x4 converted = InverseBasis * world * Basis;
                if (!TrySplitTransform(converted, out Matrix4x4 bake, out World3DTransform transform))
                    throw new InvalidDataException($"Invalid glTF transform at node {nodeIndex}.");
                JsonElement mesh = At(meshes, meshIndex);
                string name = Text(mesh, "name", $"mesh_{meshIndex}");
                int primitiveIndex = 0;
                foreach (JsonElement primitive in mesh.GetProperty("primitives").EnumerateArray())
                {
                    var key = new GeometryKey(meshIndex, primitiveIndex++, bake);
                    int materialIndex = primitive.TryGetProperty("material", out JsonElement materialElement)
                        ? materialElement.GetInt32() : -1;
                    MaterialPlan? material = materials.GetDrawMaterial(materialIndex);
                    if (material == null)
                    {
                        skippedGlass++;
                        continue;
                    }
                    if (!geometries.TryGetValue(key, out GeometryPlan? geometry))
                    {
                        geometry = ReadGeometry(root, buffers, primitive, bake, material.IsTextured, $"{name}/{key.Primitive}");
                        geometries.Add(key, geometry);
                    }
                    draws.Add(new DrawPlan(geometry, material, transform));
                    if (material.Factors.AlphaMode == World3DAlphaMode.Mask)
                        masked++;
                }
            }
            if (node.TryGetProperty("children", out JsonElement children))
            {
                foreach (JsonElement child in children.EnumerateArray())
                    Visit(child.GetInt32(), world, depth + 1);
            }
        }
    }

    private static Matrix4x4 ReadLocalTransform(JsonElement node)
    {
        if (node.TryGetProperty("matrix", out JsonElement matrix))
        {
            if (node.TryGetProperty("translation", out _) || node.TryGetProperty("rotation", out _) || node.TryGetProperty("scale", out _) ||
                matrix.GetArrayLength() != 16)
                throw new InvalidDataException("A glTF matrix must have 16 components and cannot be combined with TRS.");
            // The serialized columns are the rows of the equivalent row-vector matrix.
            return new Matrix4x4(matrix[0].GetSingle(), matrix[1].GetSingle(), matrix[2].GetSingle(), matrix[3].GetSingle(),
                matrix[4].GetSingle(), matrix[5].GetSingle(), matrix[6].GetSingle(), matrix[7].GetSingle(),
                matrix[8].GetSingle(), matrix[9].GetSingle(), matrix[10].GetSingle(), matrix[11].GetSingle(),
                matrix[12].GetSingle(), matrix[13].GetSingle(), matrix[14].GetSingle(), matrix[15].GetSingle());
        }
        Vector4 q = ReadVector4(Property(node, "rotation"), new Vector4(0, 0, 0, 1));
        Quaternion rotation = new(q.X, q.Y, q.Z, q.W);
        float length = rotation.LengthSquared();
        if (!float.IsFinite(length) || length <= 0)
            throw new InvalidDataException("Invalid glTF node rotation.");
        Vector3 scale = ReadVector3(Property(node, "scale"), Vector3.One);
        Vector3 translation = ReadVector3(Property(node, "translation"), Vector3.Zero);
        return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(rotation)) * Matrix4x4.CreateTranslation(translation);
    }

    private static bool TrySplitTransform(Matrix4x4 world, out Matrix4x4 bake, out World3DTransform transform)
    {
        bake = Matrix4x4.Identity;
        transform = default;
        float determinant = world.GetDeterminant();
        Vector3 pivot = world.Translation;
        if (!float.IsFinite(determinant) || determinant == 0 || !IsFinite(pivot) ||
            world.M14 != 0 || world.M24 != 0 || world.M34 != 0 || world.M44 != 1)
            return false;
        World3DPosition position = new(pivot.X, pivot.Y, pivot.Z);
        world.Translation = Vector3.Zero;
        if (determinant < 0)
            bake = Reflection;
        Matrix4x4 linear = bake * world;
        if (Matrix4x4.Decompose(linear, out Vector3 scale, out Quaternion rotation, out _) &&
            scale.X > 0 && scale.Y > 0 && scale.Z > 0 &&
            LinearNear(linear, Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation)))
        {
            transform = new World3DTransform(position, rotation, scale);
            return true;
        }

        // Nonuniform parent scales followed by child rotations can introduce shear.
        // Keep the world pivot instanced and bake the non-TRS linear part into the mesh.
        bake = world;
        transform = new World3DTransform(position, Quaternion.Identity, Vector3.One);
        return true;
    }

    private static bool LinearNear(Matrix4x4 a, Matrix4x4 b) =>
        Near(new Vector3(a.M11, a.M12, a.M13), new Vector3(b.M11, b.M12, b.M13)) &&
        Near(new Vector3(a.M21, a.M22, a.M23), new Vector3(b.M21, b.M22, b.M23)) &&
        Near(new Vector3(a.M31, a.M32, a.M33), new Vector3(b.M31, b.M32, b.M33));

    private static bool Near(Vector3 a, Vector3 b) => Vector3.DistanceSquared(a, b) <= a.LengthSquared() * 1e-10f;

    private static GeometryPlan ReadGeometry(JsonElement root, FileStream[] buffers, JsonElement primitive, Matrix4x4 bake,
        bool textured, string name)
    {
        if (Integer(primitive, "mode", 4) != 4 || primitive.TryGetProperty("targets", out _))
            throw new InvalidDataException($"'{name}' is not a static TRIANGLES primitive.");
        JsonElement attributes = primitive.GetProperty("attributes");
        Accessor positions = ReadAccessor(root, buffers, attributes.GetProperty("POSITION").GetInt32(), "VEC3", 3, true);
        Accessor normals = ReadAccessor(root, buffers, attributes.GetProperty("NORMAL").GetInt32(), "VEC3", 3, true);
        Accessor uv = ReadOptionalAttribute("TEXCOORD_0", "VEC2", 2);
        Accessor tangent = ReadOptionalAttribute("TANGENT", "VEC4", 4);
        Accessor indices = ReadAccessor(root, buffers, primitive.GetProperty("indices").GetInt32(), "SCALAR", 1, false);
        long bytes = (long)positions.Count * VertexPBR.SizeInBytes + (long)indices.Count * sizeof(uint);
        if (bytes > GeometryBatchBytes || indices.Count % 3 != 0 || normals.Count != positions.Count ||
            (uv.Count != 0 && uv.Count != positions.Count) || (textured && uv.Count == 0) ||
            (tangent.Count != 0 && tangent.Count != positions.Count))
            throw new InvalidDataException($"'{name}' has inconsistent vertex/index counts, no required UV0, or exceeds the geometry size limit.");
        if (!Matrix4x4.Invert(bake, out Matrix4x4 inverse))
            throw new InvalidDataException($"'{name}' has a singular baked transform.");
        return new GeometryPlan(name, positions, normals, uv, tangent, indices, bake, Matrix4x4.Transpose(inverse), (int)bytes);

        Accessor ReadOptionalAttribute(string attribute, string type, int count) => attributes.TryGetProperty(attribute, out JsonElement value)
            ? ReadAccessor(root, buffers, value.GetInt32(), type, count, true) : default;
    }

    private static Accessor ReadAccessor(JsonElement root, FileStream[] buffers, int index, string type, int components, bool attribute)
    {
        JsonElement accessor = At(root.GetProperty("accessors"), index);
        int component = accessor.GetProperty("componentType").GetInt32();
        if (Text(accessor, "type", "") != type || (attribute ? component != Float : component is not (UnsignedShort or UnsignedInt)) ||
            Property(accessor, "normalized").ValueKind == JsonValueKind.True || accessor.TryGetProperty("sparse", out _))
            throw new InvalidDataException($"Unsupported glTF accessor {index}: expected dense {type} with the Bistro component type.");

        JsonElement view = At(root.GetProperty("bufferViews"), accessor.GetProperty("bufferView").GetInt32());
        int bufferIndex = view.GetProperty("buffer").GetInt32();
        int count = accessor.GetProperty("count").GetInt32();
        int viewOffset = Integer(view, "byteOffset", 0);
        int offset = Integer(accessor, "byteOffset", 0);
        int viewLength = view.GetProperty("byteLength").GetInt32();
        int componentBytes = component == UnsignedShort ? 2 : 4;
        int elementBytes = componentBytes * components;
        int stride = Integer(view, "byteStride", elementBytes);
        long bytes = ((long)count - 1) * stride + elementBytes;
        if ((uint)bufferIndex >= (uint)buffers.Length || count <= 0 || viewOffset < 0 || offset < 0 || viewLength <= 0 ||
            stride < elementBytes || stride > 252 || stride % componentBytes != 0 || (!attribute && stride != elementBytes) ||
            offset % componentBytes != 0 || ((long)viewOffset + offset) % componentBytes != 0 ||
            (long)viewOffset + viewLength > buffers[bufferIndex].Length || offset + bytes > viewLength || bytes > MaxAccessorBytes)
            throw new InvalidDataException($"glTF accessor {index} has an invalid or oversized buffer range.");
        return new Accessor(buffers[bufferIndex], (long)viewOffset + offset, count, stride, component, (int)bytes);
    }

    private bool TryUploadGeometry(GeometryPlan plan)
    {
        var vertices = new VertexPBR[plan.Position.Count];
        var indices = new uint[plan.Index.Count];
        byte[] scratch = ArrayPool<byte>.Shared.Rent(Math.Max(plan.Position.Bytes, Math.Max(plan.Normal.Bytes,
            Math.Max(plan.Uv.Bytes, Math.Max(plan.Tangent.Bytes, plan.Index.Bytes)))));
        try
        {
            bool reverse = plan.Bake.GetDeterminant() > 0; // Basis already reflects; a second reflection cancels it.
            FillVertices(plan, vertices, scratch, reverse, out Vector3 min, out Vector3 max);
            ReadOnlySpan<byte> data = ReadData(plan.Index, scratch);
            for (int i = 0; i < indices.Length; i += 3)
            {
                uint a = ReadIndex(data, plan.Index, i);
                uint b = ReadIndex(data, plan.Index, i + 1);
                uint c = ReadIndex(data, plan.Index, i + 2);
                if (a >= vertices.Length || b >= vertices.Length || c >= vertices.Length)
                    throw new InvalidDataException($"'{plan.Name}' has an index outside its vertex array.");
                indices[i] = a;
                indices[i + 1] = reverse ? c : b;
                indices[i + 2] = reverse ? b : c;
            }
            if (plan.Tangent.Count == 0)
            {
                if (plan.Uv.Count != 0)
                    GenerateTangents(vertices, indices);
                else
                    MeshDecodeUtility.SetDefaultTangents(vertices);
            }

            if (!_scene.TryRegisterGeometry(min, max, out WorldGeometryHandle handle))
                return false;
            ModelGeometry source = ModelGeometry.Create<VertexPBR>(_system, vertices, indices, min, max, plan.Name);
            bool transferred = false;
            try
            {
                transferred = _scene.TryQueueGeometryLod(handle, 0, source);
                if (transferred)
                    plan.Handle = handle;
                return transferred;
            }
            finally
            {
                if (!transferred)
                {
                    source.Dispose();
                    _scene.TryRemoveGeometry(handle);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(scratch);
        }
    }

    private static void FillVertices(GeometryPlan plan, Span<VertexPBR> vertices, byte[] scratch, bool reverse, out Vector3 min, out Vector3 max)
    {
        min = new Vector3(float.PositiveInfinity);
        max = new Vector3(float.NegativeInfinity);
        ReadOnlySpan<byte> data = ReadData(plan.Position, scratch);
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 position = Vector3.TransformNormal(ReadBasisVector(data[(i * plan.Position.Stride)..]), plan.Bake);
            if (!IsFinite(position))
                throw new InvalidDataException($"'{plan.Name}' contains a non-finite position.");
            vertices[i].Position = position;
            min = Vector3.Min(min, position);
            max = Vector3.Max(max, position);
        }
        data = ReadData(plan.Normal, scratch);
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 normal = Vector3.TransformNormal(ReadBasisVector(data[(i * plan.Normal.Stride)..]), plan.NormalMatrix);
            vertices[i].Normal = TryNormalize(normal, out Vector3 unit) ? unit : Vector3.UnitZ;
        }
        if (plan.Uv.Count != 0)
        {
            data = ReadData(plan.Uv, scratch);
            for (int i = 0; i < vertices.Length; i++)
            {
                ReadOnlySpan<byte> value = data[(i * plan.Uv.Stride)..];
                Vector2 uv = new(ReadFloat(value), ReadFloat(value[4..]));
                if (!float.IsFinite(uv.X) || !float.IsFinite(uv.Y))
                    throw new InvalidDataException($"'{plan.Name}' contains a non-finite UV0.");
                vertices[i].UV = uv;
            }
        }
        if (plan.Tangent.Count == 0)
            return;
        data = ReadData(plan.Tangent, scratch);
        for (int i = 0; i < vertices.Length; i++)
        {
            ReadOnlySpan<byte> value = data[(i * plan.Tangent.Stride)..];
            Vector3 tangent = Vector3.TransformNormal(ReadBasisVector(value), plan.Bake);
            float sign = ReadFloat(value[12..]);
            if (sign is not (1f or -1f))
                throw new InvalidDataException($"'{plan.Name}' contains an invalid tangent sign.");
            vertices[i].Tangent = new Vector4(Orthogonalize(vertices[i].Normal, tangent), reverse ? -sign : sign);
        }
    }

    private static ReadOnlySpan<byte> ReadData(Accessor accessor, byte[] scratch)
    {
        Span<byte> data = scratch.AsSpan(0, accessor.Bytes);
        accessor.Buffer.Position = accessor.Offset;
        accessor.Buffer.ReadExactly(data);
        return data;
    }

    private static void GenerateTangents(Span<VertexPBR> vertices, ReadOnlySpan<uint> indices)
    {
        var bitangents = new Vector3[vertices.Length];
        for (int i = 0; i < indices.Length; i += 3)
        {
            int a = (int)indices[i], b = (int)indices[i + 1], c = (int)indices[i + 2];
            Vector3 ab = vertices[b].Position - vertices[a].Position;
            Vector3 ac = vertices[c].Position - vertices[a].Position;
            Vector2 uvAb = vertices[b].UV - vertices[a].UV;
            Vector2 uvAc = vertices[c].UV - vertices[a].UV;
            float determinant = uvAb.X * uvAc.Y - uvAb.Y * uvAc.X;
            if (!float.IsFinite(determinant) || MathF.Abs(determinant) <= 1e-20f)
                continue;
            Vector4 tangent = new((uvAc.Y * ab - uvAb.Y * ac) / determinant, 0);
            Vector3 bitangent = (uvAb.X * ac - uvAc.X * ab) / determinant;
            vertices[a].Tangent += tangent;
            vertices[b].Tangent += tangent;
            vertices[c].Tangent += tangent;
            bitangents[a] += bitangent;
            bitangents[b] += bitangent;
            bitangents[c] += bitangent;
        }
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 normal = vertices[i].Normal;
            Vector4 accumulated = vertices[i].Tangent;
            Vector3 tangent = Orthogonalize(normal, new Vector3(accumulated.X, accumulated.Y, accumulated.Z));
            float sign = Vector3.Dot(Vector3.Cross(normal, tangent), bitangents[i]) < 0 ? -1 : 1;
            vertices[i].Tangent = new Vector4(tangent, sign);
        }
    }

    private static Vector3 Orthogonalize(Vector3 normal, Vector3 tangent) =>
        TryNormalize(tangent - normal * Vector3.Dot(normal, tangent), out Vector3 unit) ? unit :
            Vector3.Normalize(Vector3.Cross(normal, MathF.Abs(normal.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX));

    private static bool TryNormalize(Vector3 v, out Vector3 unit)
    {
        float length = v.LengthSquared();
        unit = default;
        if (!float.IsFinite(length) || length <= 1e-20f)
            return false;
        unit = v / MathF.Sqrt(length);
        return true;
    }

    private static uint ReadIndex(ReadOnlySpan<byte> data, Accessor accessor, int index)
    {
        ReadOnlySpan<byte> element = data[(index * accessor.Stride)..];
        return accessor.Component == UnsignedShort ? BinaryPrimitives.ReadUInt16LittleEndian(element) : BinaryPrimitives.ReadUInt32LittleEndian(element);
    }

    private static float ReadFloat(ReadOnlySpan<byte> data) => BinaryPrimitives.ReadSingleLittleEndian(data);
    private static Vector3 ReadBasisVector(ReadOnlySpan<byte> data) => new(-ReadFloat(data[8..]), ReadFloat(data), ReadFloat(data[4..]));
    private static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    private static JsonElement Property(JsonElement obj, string name) => obj.ValueKind == JsonValueKind.Undefined ? default :
        obj.TryGetProperty(name, out JsonElement value) ? value : default;
    private static string Text(JsonElement obj, string name, string fallback) =>
        Property(obj, name) is { ValueKind: not JsonValueKind.Undefined } value ? value.GetString() ?? fallback : fallback;
    private static int Integer(JsonElement obj, string name, int fallback) =>
        Property(obj, name) is { ValueKind: not JsonValueKind.Undefined } value ? value.GetInt32() : fallback;
    private static float Scalar(JsonElement obj, string name, float fallback) =>
        Property(obj, name) is { ValueKind: not JsonValueKind.Undefined } value ? value.GetSingle() : fallback;

    private static JsonElement At(JsonElement array, int index) => array.ValueKind == JsonValueKind.Array && (uint)index < (uint)array.GetArrayLength()
        ? array[index] : throw new InvalidDataException($"Invalid glTF array index {index}.");

    private static Vector3 ReadVector3(JsonElement value, Vector3 fallback) => value.ValueKind == JsonValueKind.Undefined ? fallback :
        value.GetArrayLength() == 3 ? new Vector3(value[0].GetSingle(), value[1].GetSingle(), value[2].GetSingle()) :
        throw new InvalidDataException("Expected a three-component glTF vector.");

    private static Vector4 ReadVector4(JsonElement value, Vector4 fallback) => value.ValueKind == JsonValueKind.Undefined ? fallback :
        value.GetArrayLength() == 4 ? new Vector4(value[0].GetSingle(), value[1].GetSingle(), value[2].GetSingle(), value[3].GetSingle()) :
        throw new InvalidDataException("Expected a four-component glTF vector.");

    private static bool IsBadAsset(Exception ex) => ex is IOException or UnauthorizedAccessException or InvalidDataException or
        JsonException or ImageDecodeException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException;

    private static FileStream OpenAsset(string directory, string uri)
    {
        if (string.IsNullOrEmpty(uri) || Path.IsPathRooted(uri) || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"Only local, relative asset URIs are supported: '{uri}'.");
        string path = directory;
        string[] segments = uri.Split('/');
        for (int i = 0; i < segments.Length; i++)
        {
            string segment = Uri.UnescapeDataString(segments[i]);
            if (segment.Length == 0 || segment is "." or ".." || segment.EndsWith('.') || segment.EndsWith(' ') ||
                segment.IndexOfAny(['/', '\\', ':', '?', '#', '%', '\0']) >= 0 || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new InvalidDataException($"Invalid asset-local URI '{uri}'.");
            path = Path.Combine(path, segment);
            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0 || ((attributes & FileAttributes.Directory) != 0) != (i != segments.Length - 1))
                throw new InvalidDataException($"Asset URI must reference a regular file inside Bistro: '{uri}'.");
        }
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    private readonly record struct Accessor(FileStream Buffer, long Offset, int Count, int Stride, int Component, int Bytes);
    private readonly record struct GeometryKey(int Mesh, int Primitive, Matrix4x4 Bake);
    private readonly record struct DrawPlan(GeometryPlan Geometry, MaterialPlan Material, World3DTransform Transform);
    private sealed record ScenePlan(List<DrawPlan> Draws, List<GeometryPlan> Geometries, List<MaterialPlan> Materials,
        int Masked, int SkippedGlass);
    private sealed record Source(string Directory, List<FileStream> Buffers, List<DrawPlan> Draws, List<GeometryPlan> Geometries,
        List<MaterialPlan> Materials, List<TexturePlan> Textures, long TextureBytes, int Masked, int SkippedGlass, int InteriorDraws);

    private sealed record GeometryPlan(string Name, Accessor Position, Accessor Normal, Accessor Uv, Accessor Tangent,
        Accessor Index, Matrix4x4 Bake, Matrix4x4 NormalMatrix, int UploadBytes)
    {
        public WorldGeometryHandle Handle;
    }
}
