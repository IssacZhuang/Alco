using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Alco.Graphics;
using Alco.IO;
using NUnit.Framework;

namespace Alco.Rendering.Test;

/// <summary>
/// Verifies migrated asset pipeline types (<see cref="ModelGeometry"/>, <see cref="AssetLoaderMeshObj"/>,
/// <see cref="TextureAtlas"/>) and JSON converters against <see cref="Alco.Graphics"/>.
/// </summary>
[TestFixture]
public class TestAssetPipelineAndConverters
{
    /// <summary>Verifies all blend state presets round-trip through <see cref="JsonConverterBlendState"/>.</summary>
    [Test]
    public void JsonConverterBlendState_AllPresets_RoundTrip()
    {
        JsonSerializerOptions options = new()
        {
            Converters = { new JsonConverterBlendState() },
        };

        string[] presetNames =
        [
            "Opaque",
            "AlphaBlend",
            "Additive",
            "PremultipliedAlpha",
            "NonPremultipliedAlpha",
            "Multiply",
            "AlphaBlendNoAccumulation",
        ];

        foreach (string name in presetNames)
        {
            BlendState state = JsonSerializer.Deserialize<BlendState>($"\"{name}\"", options);
            string serialized = JsonSerializer.Serialize(state, options);
            Assert.That(serialized, Is.EqualTo($"\"{name}\""));
        }

        Assert.That(
            JsonSerializer.Deserialize<BlendState>("\"alphablend\"", options),
            Is.EqualTo(JsonSerializer.Deserialize<BlendState>("\"AlphaBlend\"", options)));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<BlendState>("\"UnknownPreset\"", options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<BlendState>("123", options));
    }

    /// <summary>Verifies all depth-stencil state presets round-trip through <see cref="JsonConverterDepthStencilState"/>.</summary>
    [Test]
    public void JsonConverterDepthStencilState_AllPresets_RoundTrip()
    {
        JsonSerializerOptions options = new()
        {
            Converters = { new JsonConverterDepthStencilState() },
        };

        string[] presetNames =
        [
            "None",
            "Write",
            "Read",
            "WriteReverseZ",
            "ReadReverseZ",
            "Default",
        ];

        foreach (string name in presetNames)
        {
            DepthStencilState state = JsonSerializer.Deserialize<DepthStencilState>($"\"{name}\"", options);
            string serialized = JsonSerializer.Serialize(state, options);
            Assert.That(serialized, Is.EqualTo($"\"{name}\""));
        }

        Assert.That(
            JsonSerializer.Deserialize<DepthStencilState>("\"writereversez\"", options),
            Is.EqualTo(JsonSerializer.Deserialize<DepthStencilState>("\"WriteReverseZ\"", options)));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DepthStencilState>("\"UnknownDepth\"", options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DepthStencilState>("false", options));
    }

    /// <summary>Verifies <see cref="JsonConverterShader"/> resolves and serializes <see cref="Shader"/> via <see cref="ShaderSystem"/>.</summary>
    [Test]
    public void JsonConverterShader_ResolvesAndWritesShaderName()
    {
        const string source = """
            module ConverterTestShader;
            import Core;

            [GPURoot]
            struct DrawRoot
            {
                float4 Tint;
            };

            [shader("vertex")]
            float4 MainVS(uint vertexId : SV_VertexID) : SV_Position { return float4(0, 0, 0, 1); }

            [shader("pixel")]
            float4 MainPS() : SV_Target
            {
                DrawRoot root = gpu_load_root<DrawRoot>();
                return root.Tint;
            }
            """;

        using RenderingTestHost host = Utility.CreateRenderingSystem();
        ShaderCompilerOptions compilerOptions = new()
        {
            FileResolver = path => string.Equals(path, "ConverterTestShader.slang", StringComparison.OrdinalIgnoreCase) ? source : null,
            SearchPaths = [],
        };
        using ShaderSystem shaders = host.RenderingSystem.CreateShaderSystem(compilerOptions);
        JsonSerializerOptions options = new()
        {
            Converters = { new JsonConverterShader(shaders) },
        };

        Shader? shader = JsonSerializer.Deserialize<Shader>("\"ConverterTestShader\"", options);
        Assert.That(shader, Is.Not.Null);
        Assert.That(shader!.Name, Is.EqualTo("ConverterTestShader"));
        Assert.That(JsonSerializer.Serialize(shader, options), Is.EqualTo("\"ConverterTestShader\""));
        Assert.That(JsonSerializer.Deserialize<Shader>("\"   \"", options), Is.Null);
    }

    /// <summary>Verifies <see cref="ModelGeometry"/> buffer allocation, bounds metadata, and disposal.</summary>
    [Test]
    public void ModelGeometry_CreateAndDispose_PopulatesVertexPullingBuffers()
    {
        using RenderingTestHost host = Utility.CreateRenderingSystem();
        RenderingSystem rendering = host.RenderingSystem;

        VertexPBR[] vertices =
        [
            new(new Vector3(0, 0, 0), Vector3.UnitZ, Vector2.Zero, Vector4.UnitX),
            new(new Vector3(2, 0, 0), Vector3.UnitZ, Vector2.UnitX, Vector4.UnitX),
            new(new Vector3(0, 3, 0), Vector3.UnitZ, Vector2.UnitY, Vector4.UnitX),
        ];
        uint[] indices32 = [0, 1, 2];
        ushort[] indices16 = [0, 1, 2];
        Vector3 min = new(0, 0, 0);
        Vector3 max = new(2, 3, 0);

        using (ModelGeometry geom32 = ModelGeometry.Create<VertexPBR>(rendering, vertices, indices32, min, max, "tri32"))
        {
            Assert.That(geom32.VertexCount, Is.EqualTo(3));
            Assert.That(geom32.IndexCount, Is.EqualTo(3));
            Assert.That(geom32.IndexType, Is.EqualTo(IndexType.UInt32));
            Assert.That(geom32.LocalBoundsMin, Is.EqualTo(min));
            Assert.That(geom32.LocalBoundsMax, Is.EqualTo(max));
            Assert.That(geom32.VertexBuffer.IsDisposed, Is.False);
            Assert.That(geom32.IndexBuffer.IsDisposed, Is.False);
        }

        using (ModelGeometry geom16 = ModelGeometry.Create<VertexPBR>(rendering, vertices, indices16, min, max, "tri16"))
        {
            Assert.That(geom16.IndexType, Is.EqualTo(IndexType.UInt16));
        }

        Assert.Throws<ArgumentException>(() =>
            ModelGeometry.Create<VertexPBR>(rendering, ReadOnlySpan<VertexPBR>.Empty, indices32, min, max));
    }

    /// <summary>Verifies <see cref="AssetLoaderMeshObj"/> decodes OBJ vertices into <see cref="ModelGeometry"/>.</summary>
    [Test]
    public void AssetLoaderMeshObj_DecodesObjIntoModelGeometry()
    {
        const string obj = """
            v 0 0 0
            v 1 0 0
            v 0 2 0
            f 1 2 3
            """;

        using RenderingTestHost host = Utility.CreateRenderingSystem();
        AssetLoaderMeshObj loader = new(host.RenderingSystem);
        byte[] bytes = Encoding.UTF8.GetBytes(obj);

        using ModelGeometry geometry = (ModelGeometry)loader.CreateAsset(
            new AssetLoadContext(null!, "Models/Triangle.obj", bytes, typeof(ModelGeometry)));

        Assert.That(geometry.VertexCount, Is.EqualTo(3));
        Assert.That(geometry.IndexCount, Is.EqualTo(3));
        Assert.That(geometry.IndexType, Is.EqualTo(IndexType.UInt32));
        Assert.That(geometry.LocalBoundsMin, Is.EqualTo(Vector3.Zero));
        Assert.That(geometry.LocalBoundsMax, Is.EqualTo(new Vector3(1, 2, 0)));
    }

    /// <summary>Verifies <see cref="TextureAtlas"/> wraps a GPU <see cref="Texture"/> and owns its lifetime.</summary>
    [Test]
    public void TextureAtlas_WrapsTextureAndDestroysOnDispose()
    {
        using RenderingTestHost host = Utility.CreateRenderingSystem();
        Texture atlasTexture = TextureUtility.CreateTexture(host.RenderingSystem, 16, 16, Color32.White);
        Sprite heroSprite = new("hero", atlasTexture, new Rect(0, 0, 0.5f, 0.5f));

        using (TextureAtlas atlas = new(atlasTexture, [heroSprite]))
        {
            Assert.That(atlas.Texture, Is.SameAs(atlasTexture));
            Assert.That(atlas.Count, Is.EqualTo(1));
            Assert.That(atlas.TryGetSprite("hero", out Sprite? resolved), Is.True);
            Assert.That(resolved, Is.SameAs(heroSprite));
            Assert.That(atlasTexture.IsDisposed, Is.False);
        }

        Assert.That(atlasTexture.IsDisposed, Is.True);
    }

    private sealed class MinimalAssetHost : IAssetSystemHost, IDisposable
    {
        public event Action? OnDispose;
        public void Dispose() => OnDispose?.Invoke();
        public void LogError(ReadOnlySpan<char> message) { }
        public void LogInfo(ReadOnlySpan<char> message) { }
        public void LogSuccess(ReadOnlySpan<char> message) { }
        public void LogWarning(ReadOnlySpan<char> message) { }
        public void PostToMainThread(Action action) { }
    }

    private sealed class StubFileSource : IFileSource, IDisposable
    {
        private readonly Dictionary<string, byte[]> _files = new();
        public string Name => "Stub";
        public int Priority => 0;
        public IEnumerable<string> AllFileNames => _files.Keys;
        public void AddFile(string path, byte[] bytes) => _files[path] = bytes;
        public bool TryGetData(string path, [NotNullWhen(true)] out SafeMemoryHandle data, [NotNullWhen(false)] out string? failureReason)
        {
            if (_files.TryGetValue(path, out byte[]? bytes))
            {
                data = new SafeMemoryHandle(bytes);
                failureReason = null;
                return true;
            }
            data = SafeMemoryHandle.Empty;
            failureReason = $"Not found: {path}";
            return false;
        }
        public bool TryGetStream(string path, [NotNullWhen(true)] out Stream? stream, [NotNullWhen(false)] out string? failureReason)
        {
            stream = null;
            failureReason = "Streams unsupported";
            return false;
        }
        public void Dispose() => _files.Clear();
    }

    /// <summary>Verifies <see cref="Image"/> exposes immutable import metadata, frozen sub-sprites, and implicit <see cref="Sprite"/> conversion.</summary>
    [Test]
    public void Image_ExposesImmutableMetadataSpritesAndImplicitSprite()
    {
        using RenderingTestHost host = Utility.CreateRenderingSystem();
        Texture gpuTexture = TextureUtility.CreateTexture(host.RenderingSystem, 16, 16, Color32.White);
        ImageLoadOption option = ImageLoadOption.Default with
        {
            Name = "Textures/Panel.png",
            FilterMode = Filter.Nearest,
            AddressMode = AddressMode.ClampToEdge,
            Anisotropy = 4,
            PremultiplyAlpha = true,
            SlicePadding = new Padding(2, 4, 6, 8),
        };
        var sprites = new Dictionary<string, RectInt> { ["icon"] = new RectInt(4, 4, 8, 8) };

        using Image image = ImageUtility.CreateFromTexture(gpuTexture, option, sprites, ownsResource: true);

        Assert.That(image.Texture, Is.SameAs(gpuTexture));
        Assert.That(image.Width, Is.EqualTo(16));
        Assert.That(image.Height, Is.EqualTo(16));
        Assert.That(image.FilterMode, Is.EqualTo(Filter.Nearest));
        Assert.That(image.AddressMode, Is.EqualTo(AddressMode.ClampToEdge));
        Assert.That(image.Anisotropy, Is.EqualTo(4));
        Assert.That(image.IsPremultiplied, Is.True);
        Assert.That(image.SlicePadding, Is.EqualTo(new Padding(2, 4, 6, 8)));

        Sprite implicitSprite = image;
        Assert.That(implicitSprite, Is.SameAs(image.DefaultSprite));
        Assert.That(implicitSprite.Texture, Is.SameAs(gpuTexture));
        Assert.That(implicitSprite.SlicePadding, Is.EqualTo(new Padding(2, 4, 6, 8)));
        Assert.That(implicitSprite.FilterMode, Is.EqualTo(Filter.Nearest));
        Assert.That(implicitSprite.IsPremultiplied, Is.True);
        Assert.That(implicitSprite.PixelSize, Is.EqualTo(new Vector2(16, 16)));

        Assert.That(image.TryGetSprite("icon", out Sprite? iconSprite), Is.True);
        Assert.That(iconSprite!.UvRect, Is.EqualTo(new Rect(0.25f, 0.25f, 0.5f, 0.5f)));
        Assert.That(iconSprite.PixelSize, Is.EqualTo(new Vector2(8, 8)));
        Assert.That(image.GetSprite("missing"), Is.SameAs(image.DefaultSprite));
    }

    /// <summary>Verifies <see cref="JsonConverterImage"/> normalizes empty strings to null and serializes extensionless alias paths.</summary>
    [Test]
    public void JsonConverterImage_EmptyPathReturnsNullAndSerializesAlias()
    {
        using RenderingTestHost host = Utility.CreateRenderingSystem();
        using MinimalAssetHost assetHost = new();
        AssetSystem assetSystem = new(assetHost);
        using StubFileSource source = new();
        source.AddFile("Textures/Hero.png", [1, 2, 3, 4]);
        assetSystem.AddFileSource(source);
        assetSystem.RegisterAssetLoader(new AssetLoaderImageNoGPU(host.RenderingSystem, assetSystem));

        JsonSerializerOptions options = new()
        {
            Converters = { new JsonConverterImage(assetSystem) },
        };

        Image? empty = JsonSerializer.Deserialize<Image>("\"   \"", options);
        Assert.That(empty, Is.Null);

        Image? loaded = JsonSerializer.Deserialize<Image>("\"Textures\\\\Hero.png\"", options);
        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded!.Name, Is.EqualTo("Textures/Hero.png"));

        string written = JsonSerializer.Serialize(loaded, options);
        Assert.That(written, Is.EqualTo($"\"{assetSystem.GetAliasPath("Textures/Hero.png")}\""));
    }
}
