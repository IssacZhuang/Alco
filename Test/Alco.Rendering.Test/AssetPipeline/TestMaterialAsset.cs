using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Alco;
using Alco.Graphics;
using Alco.IO;
using NUnit.Framework;

namespace Alco.Rendering.Test;

/// <summary>
/// Verifies <see cref="ShaderValue"/> coercion rules, <c>.amat</c> deserialization via
/// <see cref="AssetLoaderMaterialAsset"/>, and binding onto composed <see cref="Material"/> instances.
/// </summary>
[TestFixture]
public class TestMaterialAsset
{
    private const string TestInterfaceSlang = """
        module MatTestSurface;
        import Core;

        public interface IMatSurface
        {
            associatedtype Params;
            float4 evalColor(float2 uv, Params p);
        };
        """;

    private const string TestCustomSurfaceSlang = """
        module MatCustomSurface;
        import Core;
        import SharedSampler;
        import MatTestSurface;

        public struct CustomSurfaceParams
        {
            public GPUTextureId noiseTexture;
            public float noiseScale;
            public float edgeWidth;
            public float3 edgeColor;
            public float4 width;
        };

        public struct CustomSurfaceImpl : IMatSurface
        {
            public typedef CustomSurfaceParams Params;
            public float4 evalColor(float2 uv, Params p)
            {
                float n = gpu_texture<Texture2D>(p.noiseTexture.index)
                    .Sample(shared_sampler_linear_clamp(), uv * p.noiseScale).r;
                float a = smoothstep(0.0, max(p.edgeWidth, 0.001), n) * p.width.x;
                return float4(p.edgeColor, a);
            }
        };
        """;

    private const string TestPassTemplateSlang = """
        module MatTestPass;
        import Core;
        import MatTestSurface;

        [GPURoot]
        struct MatPassRoot<T : IMatSurface>
        {
            float4 tint;
            T.Params Material;
        };

        struct VsOut
        {
            float4 pos : SV_Position;
            float2 uv : TEXCOORD0;
        };

        [shader("vertex")]
        VsOut vsMain<T : IMatSurface>(uint vid : SV_VertexID)
        {
            VsOut o;
            float2 p = float2((vid == 1) ? 2.0 : 0.0, (vid == 2) ? 2.0 : 0.0);
            o.pos = float4(p - 1.0, 0.0, 1.0);
            o.uv = p * 0.5;
            return o;
        }

        [shader("fragment")]
        float4 psMain<T : IMatSurface>(VsOut input) : SV_Target
        {
            let root = gpu_load_root<MatPassRoot<T>>();
            T surface;
            return surface.evalColor(input.uv, root.Material);
        }
        """;

    private const string TestSpecPassTemplateSlang = """
        module MatSpecPass;
        import Core;
        import MatTestSurface;

        [GPURoot]
        struct MatPassRoot<T : IMatSurface>
        {
            float4 tint;
            T.Params Material;
        };

        struct VsOut
        {
            float4 pos : SV_Position;
            float2 uv : TEXCOORD0;
        };

        [shader("vertex")]
        VsOut vsMain<T : IMatSurface>(uint vid : SV_VertexID)
        {
            VsOut o;
            float2 p = float2((vid == 1) ? 2.0 : 0.0, (vid == 2) ? 2.0 : 0.0);
            o.pos = float4(p - 1.0, 0.0, 1.0);
            o.uv = p * 0.5;
            return o;
        }

        [shader("fragment")]
        float4 psMain<T : IMatSurface, let USE_TINT : bool>(VsOut input) : SV_Target
        {
            let root = gpu_load_root<MatPassRoot<T>>();
            T surface;
            float4 c = surface.evalColor(input.uv, root.Material);
            return USE_TINT ? c * root.tint : c;
        }
        """;

    private const string TestSharedSpecPassTemplateSlang = """
        module MatSharedSpecPass;
        import Core;
        import MatTestSurface;

        [GPURoot]
        struct MatPassRoot<T : IMatSurface>
        {
            float4 tint;
            T.Params Material;
        };

        struct VsOut
        {
            float4 pos : SV_Position;
            float2 uv : TEXCOORD0;
        };

        [shader("vertex")]
        VsOut vsMain<T : IMatSurface, let USE_TINT : bool>(uint vid : SV_VertexID)
        {
            VsOut o;
            float2 p = float2((vid == 1) ? 2.0 : 0.0, (vid == 2) ? 2.0 : 0.0);
            o.pos = float4(USE_TINT ? p * 0.5 : p - 1.0, 0.0, 1.0);
            o.uv = p * 0.5;
            return o;
        }

        [shader("fragment")]
        float4 psMain<T : IMatSurface, let USE_TINT : bool>(VsOut input) : SV_Target
        {
            let root = gpu_load_root<MatPassRoot<T>>();
            T surface;
            float4 c = surface.evalColor(input.uv, root.Material);
            return USE_TINT ? c * root.tint : c;
        }
        """;

    private static ShaderSystem CreateTestShaderSystem(RenderingSystem rendering)
    {
        Dictionary<string, string> modules = new(StringComparer.Ordinal)
        {
            ["MatTestSurface.slang"] = TestInterfaceSlang,
            ["MatCustomSurface.slang"] = TestCustomSurfaceSlang,
            ["MatTestPass.slang"] = TestPassTemplateSlang,
            ["MatSpecPass.slang"] = TestSpecPassTemplateSlang,
            ["MatSharedSpecPass.slang"] = TestSharedSpecPassTemplateSlang,
        };
        return rendering.CreateShaderSystem(new ShaderCompilerOptions
        {
            FileResolver = path => modules.TryGetValue(Path.GetFileName(path), out string? src) ? src : null,
            PathExists = path => modules.ContainsKey(Path.GetFileName(path)),
        });
    }

    private static RootField FindField(RootLayout root, string name)
        => root.Find(name) ?? throw new AssertionException($"Root field '{name}' not found.");

    private sealed class AssetHost : IAssetSystemHost, IDisposable
    {
        public event Action? OnDispose;

        public void PostToMainThread(Action action) => action();
        public void LogInfo(ReadOnlySpan<char> message) { }
        public void LogWarning(ReadOnlySpan<char> message) { }
        public void LogError(ReadOnlySpan<char> message) { }
        public void LogSuccess(ReadOnlySpan<char> message) { }
        public void Dispose() => OnDispose?.Invoke();
    }

    [Test]
    public void JsonConverterShaderValue_ReadsAllShapes_AndCoercesToTargetLayouts()
    {
        using RenderingTestHost host = Utility.CreateRenderingSystem();
        using ShaderSystem shaders = CreateTestShaderSystem(host.RenderingSystem);
        Shader shader = shaders.Compose("MatTestPass", "MatCustomSurface");
        ShaderReflection refl = shader.Reflection;

        JsonSerializerOptions jsonOpts = new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonConverterShaderValue() }
        };

        Dictionary<string, ShaderValue>? parsed = JsonSerializer.Deserialize<Dictionary<string, ShaderValue>>("""
            {
                "noiseScale": 3,
                "edgeColor": "#FF8000",
                "width": [0.45, 1.0]
            }
            """, jsonOpts);
        Assert.That(parsed, Is.Not.Null);

        ShaderFieldLayout noiseScaleField = refl.Root!.Find("Material.noiseScale")!;
        Span<byte> scalarBuf = stackalloc byte[4];
        Assert.That(parsed!["noiseScale"].TryWriteTo(noiseScaleField.Type, scalarBuf), Is.True);
        Assert.That(MemoryMarshal.Read<float>(scalarBuf), Is.EqualTo(3.0f));

        ShaderFieldLayout edgeColorField = refl.Root.Find("Material.edgeColor")!;
        Span<byte> vec3Buf = stackalloc byte[12];
        Assert.That(parsed["edgeColor"].TryWriteTo(edgeColorField.Type, vec3Buf), Is.True);
        Vector3 color = MemoryMarshal.Read<Vector3>(vec3Buf);
        Assert.That(color.X, Is.EqualTo(1.0f).Within(0.01f));
        Assert.That(color.Y, Is.EqualTo(128f / 255f).Within(0.01f));
        Assert.That(color.Z, Is.EqualTo(0.0f).Within(0.001f));

        ShaderFieldLayout widthField = refl.Root.Find("Material.width")!;
        Span<byte> vec4Buf = stackalloc byte[16];
        Assert.That(parsed["width"].TryWriteTo(widthField.Type, vec4Buf), Is.True);
        Vector4 width = MemoryMarshal.Read<Vector4>(vec4Buf);
        Assert.That(width, Is.EqualTo(new Vector4(0.45f, 1.0f, 0.0f, 0.0f)));

        ShaderValue scalar = 1.5f;
        Assert.That(scalar.TryWriteTo(widthField.Type, vec4Buf, out string? err), Is.False);
        Assert.That(err, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void MaterialAssetLoader_ParsesWithAndWithoutTypeDiscriminator_AndAppliesToMaterial()
    {
        using RenderingTestHost host = Utility.CreateRenderingSystem();
        using ShaderSystem shaders = CreateTestShaderSystem(host.RenderingSystem);

        string tempDir = Path.Combine(Path.GetTempPath(), "alco_mat_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string pngPath = Path.Combine(tempDir, "Noise.png");
            File.Copy(Path.Combine("Files", "Image", "Png", "basn6a08.png"), pngPath);

            File.WriteAllText(Path.Combine(tempDir, "Dissolve.amat"), """
                {
                    "$type": "Alco.Rendering.MaterialAsset",
                    "version": "1.0",
                    "surface": "MatCustomSurface",
                    "textures": { "noiseTexture": "Noise" },
                    "parameters": {
                        "noiseScale": 2.5,
                        "edgeWidth": 0.2,
                        "edgeColor": "#FF7700",
                        "width": [0.45, 1.0]
                    }
                }
                """, Encoding.UTF8);

            File.WriteAllText(Path.Combine(tempDir, "DissolveSpec.amat"), """
                {
                    "$type": "Alco.Rendering.MaterialAsset",
                    "version": "1.0",
                    "surface": "MatCustomSurface",
                    "textures": { "noiseTexture": "Noise" },
                    "parameters": { "noiseScale": 2.5 },
                    "specializations": { "USE_TINT": true }
                }
                """, Encoding.UTF8);

            File.WriteAllText(Path.Combine(tempDir, "SmokeLike.amat"), """
                {
                    "Version": "1.0",
                    "Name": "CustomSmoke",
                    "Surface": "MatCustomSurface",
                    "Parameters": {
                        "noiseScale": 1,
                        "width": [0.25, 0.5, 0.75]
                    }
                }
                """, Encoding.UTF8);

            File.WriteAllText(Path.Combine(tempDir, "BadVersion.amat"), """
                {
                    "version": "99.0"
                }
                """, Encoding.UTF8);

            using AssetHost assetHost = new();
            AssetSystem assets = new(assetHost);
            assets.AddFileSource(new DirectoryFileSource(tempDir));
            assets.RegisterAssetLoader(new AssetLoaderImageNoGPU(host.RenderingSystem, assets));
            assets.RegisterAssetLoader(new AssetLoaderMaterialAsset(assets));

            MaterialAsset dissolve = assets.Load<MaterialAsset>("Dissolve");
            Assert.That(dissolve.Name, Is.EqualTo("Dissolve"));
            Assert.That(dissolve.Surface, Is.EqualTo("MatCustomSurface"));
            Assert.That(dissolve.SourceFile, Is.EqualTo("Dissolve.amat"));
            Assert.That(dissolve.Textures.ContainsKey("noiseTexture"), Is.True);

            Shader composed = shaders.Compose("MatTestPass", dissolve.Surface!);
            Material runtimeMat = composed.CreateMaterial("DissolveMat");
            dissolve.ApplyTo(runtimeMat);

            Shader specShader = shaders.Compose("MatSpecPass", dissolve.Surface!);
            Material specMat = specShader.CreateMaterial("SpecMat", false);
            MaterialAsset dissolveSpec = assets.Load<MaterialAsset>("DissolveSpec");
            dissolveSpec.ApplyTo(specMat);
            Assert.That(specMat.Specializations, Is.EqualTo(new[] { "true" }));

            byte[] rootBytes = new byte[runtimeMat.Root.SizeBytes];
            runtimeMat.CopyRoot(rootBytes);

            RootField texField = FindField(runtimeMat.Root, "Material.noiseTexture");
            Assert.That(MemoryMarshal.Read<uint>(rootBytes.AsSpan((int)texField.Offset, 4)),
                Is.EqualTo(dissolve.Textures["noiseTexture"].Texture.SampledIndex));

            RootField edgeColorField = FindField(runtimeMat.Root, "Material.edgeColor");
            Vector3 edgeColor = MemoryMarshal.Read<Vector3>(rootBytes.AsSpan((int)edgeColorField.Offset, 12));
            Assert.That(edgeColor.X, Is.EqualTo(1.0f).Within(0.01f));
            Assert.That(edgeColor.Y, Is.EqualTo(0x77 / 255f).Within(0.01f));
            Assert.That(edgeColor.Z, Is.EqualTo(0.0f).Within(0.001f));

            MaterialAsset smokeLike = assets.Load<MaterialAsset>("SmokeLike");
            Assert.That(smokeLike.Name, Is.EqualTo("CustomSmoke"));
            Material smokeMat = composed.CreateMaterial("SmokeMat");
            smokeLike.ApplyTo(smokeMat);
            smokeMat.CopyRoot(rootBytes);
            RootField widthField = FindField(smokeMat.Root, "Material.width");
            Assert.That(MemoryMarshal.Read<Vector4>(rootBytes.AsSpan((int)widthField.Offset, 16)),
                Is.EqualTo(new Vector4(0.25f, 0.5f, 0.75f, 0.0f)));

            AssetLoadException ex = Assert.Throws<AssetLoadException>(() => assets.Load<MaterialAsset>("BadVersion"))!;
            Assert.That(ex.Message, Does.Contain("99.0"));

            MaterialAsset badSlot = new()
            {
                Name = "BadSlot",
                Textures = new Dictionary<string, Image> { ["missingSlot"] = dissolve.Textures["noiseTexture"] },
            };
            Assert.Throws<InvalidOperationException>(() => badSlot.ApplyTo(runtimeMat));

            MaterialAsset badParam = new()
            {
                Name = "BadParam",
                Parameters = new Dictionary<string, ShaderValue> { ["missingParam"] = 1.0f },
            };
            Assert.Throws<InvalidOperationException>(() => badParam.ApplyTo(runtimeMat));

            // MaterialAsset.CreateMaterial specializes templates directly without creating an intermediate variant.
            Material directSpecMat = dissolveSpec.CreateMaterial(specShader, "DirectSpec");
            Assert.That(directSpecMat.Specializations, Is.EqualTo(new[] { "true" }));

            // Multi-entry-point templates sharing a value parameter name populate one slot per stage without deduplication.
            Shader sharedSpecShader = shaders.Compose("MatSharedSpecPass", dissolve.Surface!);
            Material sharedSpecMat = dissolveSpec.CreateMaterial(sharedSpecShader, "SharedSpec");
            Assert.That(sharedSpecMat.Specializations, Is.EqualTo(new[] { "true", "true" }));

            MaterialAsset sharedSpecFalse = new()
            {
                Name = "SharedFalse",
                Specializations = new Dictionary<string, ShaderValue> { ["USE_TINT"] = false },
            };
            sharedSpecFalse.ApplyTo(sharedSpecMat);
            Assert.That(sharedSpecMat.Specializations, Is.EqualTo(new[] { "false", "false" }));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Test]
    public void MaterialAsset_NullDictionariesAndShaderValueEdgeCases_BehaveSafely()
    {
        using RenderingTestHost host = Utility.CreateRenderingSystem();
        using ShaderSystem shaders = CreateTestShaderSystem(host.RenderingSystem);

        using AssetHost assetHost = new();
        AssetSystem assets = new(assetHost);
        JsonSerializerOptions jsonOpts = AssetLoaderMaterialAsset.CreateJsonOptions(assets);

        MaterialAsset? parsedNulls = JsonSerializer.Deserialize<MaterialAsset>("""
            {
                "version": "1.0",
                "name": null,
                "surface": "   ",
                "textures": null,
                "parameters": null,
                "specializations": null
            }
            """, jsonOpts);
        Assert.That(parsedNulls, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(parsedNulls!.Name, Is.EqualTo(string.Empty));
            Assert.That(parsedNulls.Surface, Is.Null);
            Assert.That(parsedNulls.Textures, Is.Empty);
            Assert.That(parsedNulls.Parameters, Is.Empty);
            Assert.That(parsedNulls.Specializations, Is.Empty);
        });

        Shader shader = shaders.Compose("MatTestPass", "MatCustomSurface");
        ShaderTypeLayout intMatSrc = new()
        {
            Kind = ShaderTypeKind.Matrix,
            ScalarType = ShaderScalarType.Float32,
            MatrixRows = 4,
            MatrixColumns = 4,
            SizeBytes = 64,
        };
        Span<byte> matBytes = stackalloc byte[64];
        int[] matrixInts = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16];
        Assert.That(ShaderValue.Ints(matrixInts).TryWriteTo(intMatSrc, matBytes), Is.True);
        Assert.That(MemoryMarshal.Read<float>(matBytes[..4]), Is.EqualTo(1f));
        Assert.That(MemoryMarshal.Read<float>(matBytes.Slice(60, 4)), Is.EqualTo(16f));

        ShaderValue badMatrixValue = ShaderValue.Bool(true);
        Assert.That(badMatrixValue.TryWriteTo(intMatSrc, matBytes, out _), Is.False);

        ShaderTypeLayout int64Type = new() { Kind = ShaderTypeKind.Scalar, ScalarType = ShaderScalarType.Int64, SizeBytes = 8 };
        ShaderTypeLayout uint64Type = new() { Kind = ShaderTypeKind.Scalar, ScalarType = ShaderScalarType.UInt64, SizeBytes = 8 };
        Span<byte> s8 = stackalloc byte[8];
        Assert.That(ShaderValue.Float(9223372036854775808.0f).TryWriteTo(int64Type, s8, out _), Is.False);
        Assert.That(ShaderValue.Float(18446744073709551616.0f).TryWriteTo(uint64Type, s8, out _), Is.False);

        JsonSerializerOptions convOpts = new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonConverterShaderValue() },
        };
        ShaderValue bigUint = JsonSerializer.Deserialize<ShaderValue>("4294967295", convOpts);
        Assert.That(bigUint.Kind, Is.EqualTo(ShaderValueKind.UInt32));
        Assert.That(bigUint.GetUInt(), Is.EqualTo(uint.MaxValue));
        Assert.That(JsonSerializer.Serialize(bigUint, convOpts), Is.EqualTo("4294967295"));

        Assert.That(JsonSerializer.Serialize(ShaderValue.Floats([]), convOpts), Is.EqualTo("[]"));
        Assert.That(JsonSerializer.Serialize(ShaderValue.Ints([]), convOpts), Is.EqualTo("[]"));
        Assert.That(JsonSerializer.Serialize(ShaderValue.UInts([]), convOpts), Is.EqualTo("[]"));
        Assert.That(JsonSerializer.Serialize(default(ShaderValue), convOpts), Is.EqualTo("false"));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ShaderValue>("{}", convOpts));
        _ = shader;
    }
}
