using System.Buffers;
using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Alco.Graphics;
using Alco.Rendering;
using Alco.World3D;

internal sealed partial class BistroImporter
{
    private const int TextureBatchBytes = 24 * 1024 * 1024;
    private const int TexturesPerBatch = 32;
    private const int MaxDdsExtent = 4096;
    private const int MaxSoftwareExtent = 512;
    private const long MaxSoftwareResidentBytes = 512L * 1024 * 1024;

    private void UploadTextureBatch()
    {
        int remaining = TextureBatchBytes;
        for (int count = 0; _nextTexture < _source.Textures.Count && count < TexturesPerBatch; count++, _nextTexture++)
        {
            TexturePlan texture = _source.Textures[_nextTexture];
            if (texture.UploadBytes > remaining)
                break;
            if (!TryUploadTexture(texture, out texture.Handle))
                throw new InvalidDataException($"World3D could not create Bistro texture '{texture.Uri}' ({texture.Format}).");
            remaining -= texture.UploadBytes;
        }
    }

    private unsafe bool TryUploadTexture(TexturePlan texture, out WorldTextureHandle handle)
    {
        using FileStream file = OpenAsset(_source.Directory, texture.Uri);
        file.Position = texture.FileOffset;
        byte[] encoded = ArrayPool<byte>.Shared.Rent(texture.EncodedBytes);
        try
        {
            Span<byte> data = encoded.AsSpan(0, texture.EncodedBytes);
            file.ReadExactly(data);
            TextureDesc desc = new()
            {
                Format = texture.Format,
                Extent = new Extent3D((uint)texture.Width, (uint)texture.Height, 1),
                MipLevels = (uint)texture.Levels,
            };
            if (texture.Format is not (Format.Rgba8Unorm or Format.Rgba8Srgb))
                return _scene.TryCreateTexture(desc, data, out handle);

            byte[] decoded = ArrayPool<byte>.Shared.Rent(texture.UploadBytes);
            try
            {
                int offset = 0;
                for (int mip = 0; mip < texture.Levels; mip++)
                {
                    int byteCount = RgbaMipBytes(texture.Width, texture.Height, mip);
                    byte* pixels = BcDecoder.DecodeLevel(data, 0, texture.Family, texture.Width, texture.Height, mip);
                    try
                    {
                        new ReadOnlySpan<byte>(pixels, byteCount).CopyTo(decoded.AsSpan(offset, byteCount));
                    }
                    finally
                    {
                        NativeMemory.Free(pixels);
                    }
                    offset += byteCount;
                }
                return _scene.TryCreateTexture(desc, decoded.AsSpan(0, offset), out handle);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(decoded);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(encoded);
        }
    }

    private static TexturePlan ReadDds(string directory, string uri, bool srgb, bool softwareBc)
    {
        if (!Path.GetExtension(uri).Equals(".dds", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Bistro core textures must be external DDS files: '{uri}'.");
        using FileStream file = OpenAsset(directory, uri);
        Span<byte> header = stackalloc byte[148];
        header = header[..(int)Math.Min(header.Length, file.Length)];
        file.ReadExactly(header);
        if (header.Length < 128 || !DdsDecoder.IsDds(header))
            throw new InvalidDataException($"'{uri}' is not a complete DDS header.");
        if ((BinaryPrimitives.ReadUInt32LittleEndian(header[112..]) & 0x0020FE00u) != 0 ||
            BinaryPrimitives.ReadUInt32LittleEndian(header[24..]) > 1 ||
            (DdsDecoder.HasDx10Header(header) && (header.Length < 148 ||
                BinaryPrimitives.ReadUInt32LittleEndian(header[132..]) != 3 ||
                (BinaryPrimitives.ReadUInt32LittleEndian(header[136..]) & 4) != 0 ||
                BinaryPrimitives.ReadUInt32LittleEndian(header[140..]) != 1)))
            throw new InvalidDataException($"'{uri}' must be a single-layer 2D DDS.");

        int width = BinaryPrimitives.ReadInt32LittleEndian(header[16..]);
        int height = BinaryPrimitives.ReadInt32LittleEndian(header[12..]);
        uint declaredLevels = BinaryPrimitives.ReadUInt32LittleEndian(header[28..]);
        if (width is < 1 or > MaxDdsExtent || height is < 1 or > MaxDdsExtent ||
            declaredLevels > 1 + BitOperations.Log2((uint)Math.Max(width, height)))
            throw new InvalidDataException($"'{uri}' has invalid or oversized DDS dimensions/mip count.");
        int levels = Math.Max(1, (int)declaredLevels);

        // Reuse format detection, but keep the real dimensions and all block-padded mips.
        // The older DDS helper reports only a block-aligned prefix for legacy uploads.
        DdsDecoder.BcFamily family;
        int offset;
        try
        {
            DdsDecoder.ParseHeader(header, srgb, out family, out _, out _, out _, out _, out offset);
        }
        catch (ImageDecodeException ex)
        {
            throw new InvalidDataException($"DDS '{uri}': {ex.Message}", ex);
        }
        Format format = family switch
        {
            DdsDecoder.BcFamily.BC1 => srgb ? Format.Bc1Srgb : Format.Bc1Unorm,
            DdsDecoder.BcFamily.BC2 => srgb ? Format.Bc2Srgb : Format.Bc2Unorm,
            DdsDecoder.BcFamily.BC3 => srgb ? Format.Bc3Srgb : Format.Bc3Unorm,
            DdsDecoder.BcFamily.BC5 when !srgb => Format.Bc5Rg,
            _ => throw new InvalidDataException($"Unsupported Bistro DDS compression {family} in '{uri}'."),
        };
        uint blockBytes = DdsDecoder.GetBlockBytes(family);
        int firstMip = 0;
        int uploadBytes = 0;
        if (softwareBc)
        {
            // Reuse authored smaller mips instead of decoding or allocating the full-resolution chain.
            while (firstMip < levels && (Math.Max(width, height) >> firstMip) > MaxSoftwareExtent)
            {
                offset += (int)DdsDecoder.GetMipByteCount(width, height, firstMip, blockBytes);
                firstMip++;
            }
            if (firstMip == levels)
                throw new InvalidDataException($"'{uri}' has no DDS mip small enough for the software BC limit ({MaxSoftwareExtent}px).");
            format = srgb ? Format.Rgba8Srgb : Format.Rgba8Unorm;
        }
        width = Math.Max(1, width >> firstMip);
        height = Math.Max(1, height >> firstMip);
        levels -= firstMip;
        int encodedBytes = 0;
        for (int mip = 0; mip < levels; mip++)
        {
            encodedBytes += (int)DdsDecoder.GetMipByteCount(width, height, mip, blockBytes);
            if (softwareBc)
                uploadBytes += RgbaMipBytes(width, height, mip);
        }
        if (file.Length < (long)offset + encodedBytes)
            throw new InvalidDataException($"'{uri}' has a truncated DDS mip chain.");
        if (!softwareBc)
            uploadBytes = encodedBytes;
        if (uploadBytes > TextureBatchBytes)
            throw new InvalidDataException($"'{uri}' exceeds the texture upload batch limit.");
        return new TexturePlan(uri, family, format, width, height, levels, offset, encodedBytes, uploadBytes);
    }

    private static int RgbaMipBytes(int width, int height, int mip) => Math.Max(1, width >> mip) * Math.Max(1, height >> mip) * 4;

    private sealed class TextureCatalog(string directory)
    {
        private readonly Dictionary<(string Uri, bool Srgb), TexturePlan> _plans = new();

        public bool SoftwareBc { get; init; }
        public List<TexturePlan> Plans { get; } = [];
        public long UploadBytes { get; private set; }

        public TexturePlan GetPlan(string uri, bool srgb)
        {
            var key = (uri, srgb);
            if (_plans.TryGetValue(key, out TexturePlan? cached))
                return cached;

            TexturePlan plan = ReadDds(directory, uri, srgb, SoftwareBc);
            UploadBytes += plan.UploadBytes;
            if (SoftwareBc && UploadBytes > MaxSoftwareResidentBytes)
                throw new InvalidDataException($"Combined Bistro exceeds the {MaxSoftwareResidentBytes / (1024 * 1024)} MiB software BC texture budget.");
            _plans.Add(key, plan);
            Plans.Add(plan);
            return plan;
        }
    }

    private sealed class MaterialReader(JsonElement root, TextureCatalog textures)
    {
        private const float BlenderUniformRoughnessArtifact = 0.5527864f;
        private const float BlenderRoughnessArtifactTolerance = 1e-4f;
        private readonly Dictionary<int, MaterialPlan?> _materials = new();
        private readonly bool _ignoreUniformRoughnessArtifact = HasUniformBlenderRoughnessArtifact(root);

        public bool LegacyMetallic { get; init; }
        public List<MaterialPlan> DrawMaterials { get; } = [];

        public MaterialPlan? GetDrawMaterial(int index)
        {
            if (_materials.TryGetValue(index, out MaterialPlan? material))
                return material;

            JsonElement value = index == -1 ? default : At(Property(root, "materials"), index);
            string name = Text(value, "name", index == -1 ? "glTF default" : $"material_{index}");
            string alpha = Text(value, "alphaMode", "OPAQUE");
            // Re-enable Bistro glass with physical Blend routing; foliage/wire BLEND entries use Mask cutoff.
            World3DAlphaMode alphaMode = alpha switch
            {
                "OPAQUE" => World3DAlphaMode.Opaque,
                "MASK" => World3DAlphaMode.Mask,
                "BLEND" => name.Contains("Glass", StringComparison.OrdinalIgnoreCase)
                    ? World3DAlphaMode.Blend : World3DAlphaMode.Mask,
                _ => throw new InvalidDataException($"Invalid alpha mode '{alpha}' on '{name}'."),
            };
            float ior = 0f;
            float transmission = 0f;
            if (value.TryGetProperty("extensions", out JsonElement ext))
            {
                if (ext.TryGetProperty("KHR_materials_ior", out JsonElement iorExt))
                    ior = Math.Clamp(Scalar(iorExt, "ior", 1.5f), 1f, 3f);
                if (ext.TryGetProperty("KHR_materials_transmission", out JsonElement transExt))
                    transmission = Math.Clamp(Scalar(transExt, "transmissionFactor", 0f), 0f, 1f);
            }
            if (alphaMode == World3DAlphaMode.Blend && transmission <= 0f)
                transmission = 0.85f;
            JsonElement pbr = Property(value, "pbrMetallicRoughness");
            TexturePlan? baseColor = ReadTexture(Property(pbr, "baseColorTexture"), srgb: true);
            TexturePlan? roughness = ReadTexture(Property(pbr, "metallicRoughnessTexture"), srgb: false);
            JsonElement normalInfo = Property(value, "normalTexture");
            if (Scalar(normalInfo, "scale", 1) != 1)
                throw new InvalidDataException($"Material '{name}' has an unsupported normal scale.");
            TexturePlan? normal = ReadTexture(normalInfo, srgb: false, normal: true);
            TexturePlan? emissive = ReadTexture(Property(value, "emissiveTexture"), srgb: true);
            var factors = new World3DMaterialDesc
            {
                BaseColor = ReadVector4(Property(pbr, "baseColorFactor"), Vector4.One),
                Metallic = Scalar(pbr, "metallicFactor", LegacyMetallic && roughness != null ? 0 : 1),
                Roughness = _ignoreUniformRoughnessArtifact ? 1f : Scalar(pbr, "roughnessFactor", 1),
                Emissive = ReadVector3(Property(value, "emissiveFactor"), Vector3.Zero),
                AlphaMode = alphaMode,
                AlphaCutoff = alpha == "BLEND" ? 0.5f : Scalar(value, "alphaCutoff", 0.5f),
                DoubleSided = Property(value, "doubleSided") is { ValueKind: not JsonValueKind.Undefined } twoSided && twoSided.GetBoolean(),
                Sampler = World3DMaterialSampler.LinearRepeat,
                Transmission = transmission,
                Ior = ior,
            };
            material = new MaterialPlan(name, factors, baseColor, normal, roughness, emissive);
            _materials.Add(index, material);
            DrawMaterials.Add(material);
            return material;
        }

        private TexturePlan? ReadTexture(JsonElement info, bool srgb, bool normal = false)
        {
            if (info.ValueKind == JsonValueKind.Undefined)
                return null;
            if (Integer(info, "texCoord", 0) != 0 || info.TryGetProperty("extensions", out _))
                throw new InvalidDataException("Bistro supports only untransformed UV0 material slots.");
            JsonElement texture = At(Property(root, "textures"), info.GetProperty("index").GetInt32());
            if (texture.TryGetProperty("sampler", out JsonElement samplerIndex))
            {
                JsonElement sampler = At(Property(root, "samplers"), samplerIndex.GetInt32());
                if (Integer(sampler, "magFilter", 9729) != 9729 || Integer(sampler, "minFilter", 9987) != 9987 ||
                    Integer(sampler, "wrapS", 10497) != 10497 || Integer(sampler, "wrapT", 10497) != 10497)
                    throw new InvalidDataException("Bistro supports only the shared trilinear repeat sampler.");
            }
            JsonElement image = At(Property(root, "images"), texture.GetProperty("source").GetInt32());
            string uri = Text(image, "uri", "");
            TexturePlan plan = textures.GetPlan(uri, srgb);
            if (!normal && plan.Family == DdsDecoder.BcFamily.BC5)
                throw new InvalidDataException($"BC5 is only supported for a normal slot: '{uri}'.");
            return plan;
        }

        private static bool HasUniformBlenderRoughnessArtifact(JsonElement root)
        {
            JsonElement materials = Property(root, "materials");
            if (materials.ValueKind != JsonValueKind.Array || materials.GetArrayLength() < 2)
                return false;

            foreach (JsonElement material in materials.EnumerateArray())
            {
                JsonElement pbr = Property(material, "pbrMetallicRoughness");
                if (pbr.ValueKind != JsonValueKind.Object ||
                    !pbr.TryGetProperty("roughnessFactor", out JsonElement roughnessElement) ||
                    roughnessElement.ValueKind != JsonValueKind.Number)
                    return false;

                float factor = roughnessElement.GetSingle();
                if (!(MathF.Abs(factor - BlenderUniformRoughnessArtifact) <= BlenderRoughnessArtifactTolerance))
                    return false;
            }
            return true;
        }
    }

    private sealed record TexturePlan(string Uri, DdsDecoder.BcFamily Family, Format Format,
        int Width, int Height, int Levels, int FileOffset, int EncodedBytes, int UploadBytes)
    {
        public WorldTextureHandle Handle;
    }

    private sealed record MaterialPlan(string Name, World3DMaterialDesc Factors, TexturePlan? BaseColor, TexturePlan? Normal,
        TexturePlan? Roughness, TexturePlan? Emissive)
    {
        public WorldMaterialHandle Handle;
        public bool IsTextured => BaseColor != null || Normal != null || Roughness != null || Emissive != null;
        public World3DMaterialDesc Description => Factors with
        {
            BaseColorTexture = BaseColor?.Handle ?? default,
            NormalTexture = Normal?.Handle ?? default,
            MetallicRoughnessTexture = Roughness?.Handle ?? default,
            EmissiveTexture = Emissive?.Handle ?? default,
        };
    }
}
