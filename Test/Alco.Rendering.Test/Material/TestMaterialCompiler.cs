#nullable enable

using System.Numerics;
using System.Text;
using Alco.Graphics;
using Alco.ShaderCompiler;
using NUnit.Framework;

namespace Alco.Rendering.Test;

// ─────────────────────────────────────────────────────────────────────────────
// MaterialCompiler tests: the pipeline-agnostic registry — pass registration,
// (asset, pass) caching, Accepts routing (including the checked asset-family cast
// of IMaterialPass<TAsset>), texture-slot/parameter validation against the
// composed reflection, the default-surface rule and the asset-driven fallback
// texture policy. Runs on the NoGPU device with in-memory slang modules,
// mirroring MaterialComposerTest.
// ─────────────────────────────────────────────────────────────────────────────
[TestFixture]
public class TestMaterialCompiler
{
    /// <summary>A pipeline-family asset for the tests below (the compiler's TAsset).</summary>
    private sealed class TestMaterialAsset : MaterialAsset
    {
        /// <summary>The family policy: flat normal for normal maps, black for emissive.</summary>
        public override MaterialTextureFallback GetTextureFallback(string slot)
        {
            if (slot.StartsWith("normal", StringComparison.OrdinalIgnoreCase))
            {
                return MaterialTextureFallback.FlatNormal;
            }
            if (slot.StartsWith("emissive", StringComparison.OrdinalIgnoreCase))
            {
                return MaterialTextureFallback.Black;
            }
            return MaterialTextureFallback.White;
        }
    }

    /// <summary>A minimal materializing pass typed to the test asset family.</summary>
    private class StubPass(string id, string templateModule, RenderingSystem rendering)
        : IMaterialPass<TestMaterialAsset>
    {
        public string Id => id;
        public ShaderLibrary Template => rendering.ShaderSystem.GetLibrary(templateModule);
        public virtual bool Accepts(TestMaterialAsset asset) => true;
        public GraphicsMaterial CreateMaterial(TestMaterialAsset asset, Shader shader)
            => rendering.CreateGraphicsMaterial(shader, $"{asset.Name}_{id}");
    }

    /// <summary>A pass accepting nothing (a disabled feature's optional pass).</summary>
    private sealed class RejectingPass(string id, RenderingSystem rendering)
        : StubPass(id, "test_lit_template", rendering)
    {
        public override bool Accepts(TestMaterialAsset asset) => false;
    }

    private const string Contract = """
        #language slang 2025
        module test_compiler_contract;

        public struct SurfaceInput
        {
            public float2 uv;
        }

        // Marks a cbuffer as a material-parameter block (the engine discovers
        // parameter blocks by this marker, not by name).
        [__AttributeUsage(_AttributeTargets.Var)]
        public struct MaterialParams {};

        public interface ISurface
        {
            float4 GetBaseColor(SurfaceInput input) { return float4(1, 1, 1, 1); }
        }
        """;

    private const string LitTemplate = """
        #language slang 2025
        module test_lit_template;

        import test_compiler_contract;

        cbuffer _camera : register(b0, space0)
        {
            float4x4 viewProjection;
        }

        cbuffer _draw : register(b0, space1)
        {
            RWStructuredBuffer<float4> _instances;
        }

        public struct LitV2F
        {
            public float4 position : SV_POSITION;
            public float2 uv : TEXCOORD0;
        }

        [shader("vertex")]
        public LitV2F MainVS<T : ISurface>(float3 position : POSITION, float2 uv : TEXCOORD0)
        {
            LitV2F output;
            output.position = mul(viewProjection, float4(position + _instances[0].xyz, 1.0));
            output.uv = uv;
            return output;
        }

        [shader("fragment")]
        public float4 MainPS<T : ISurface>(LitV2F input) : SV_TARGET
        {
            T surface = T();
            SurfaceInput surfaceInput;
            surfaceInput.uv = input.uv;
            return surface.GetBaseColor(surfaceInput);
        }
        """;

    private const string Surface = """
        #language slang 2025
        module test_compiler_surface;

        import test_compiler_contract;

        cbuffer _material : register(b0, space2)
        {
            Texture2D<float4> _albedoTexture;
            SamplerState _albedoTextureSampler;
        }

        [MaterialParams]
        cbuffer _surfaceParams : register(b1, space2)
        {
            float pulseSpeed;
        }

        public struct Surface : ISurface
        {
            public override float4 GetBaseColor(SurfaceInput input)
            {
                return _albedoTexture.Sample(_albedoTextureSampler, input.uv) * pulseSpeed;
            }
        }
        """;

    private static readonly Dictionary<string, string> Files = new()
    {
        ["test-compiler-contract.slang"] = Contract,
        ["test-lit-template.slang"] = LitTemplate,
        ["test-compiler-surface.slang"] = Surface,
    };

    /// <summary>A rendering system whose shader modules resolve from <see cref="Files"/>.</summary>
    private static DummyRenderingSystemHost CreateRenderingSystem()
    {
        DummyRenderingSystemHost host = Utility.CreateRenderingSystem();
        host.RenderingSystem.SetShaderModuleResolver(ShaderModuleResolver.Create(
            path =>
            {
                // Imports probe several module-name→file forms; match on the dashed form.
                string key = SlangPathUtility.NormalizePath(path).Replace('/', '-').Replace('_', '-');
                foreach (KeyValuePair<string, string> file in Files)
                {
                    if (key.EndsWith(file.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        return new MemoryStream(Encoding.UTF8.GetBytes(file.Value));
                    }
                }
                return null;
            },
            () => Files.Keys));
        return host;
    }

    [Test]
    public void RegisterPassRejectsDuplicateIds()
    {
        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem();
        using MaterialCompiler compiler = new(host.RenderingSystem);

        compiler.RegisterPass(new StubPass("main", "test_lit_template", host.RenderingSystem));

        Assert.Multiple(() =>
        {
            Assert.That(() => compiler.RegisterPass(new StubPass("main", "test_lit_template", host.RenderingSystem)),
                Throws.ArgumentException, "A second pass under a live id is rejected.");
            Assert.That(() => compiler.RegisterPass(new StubPass("other", "test_lit_template", host.RenderingSystem)),
                Throws.Nothing);
        });
    }

    [Test]
    public void UnregisteredPassReportsUnusable()
    {
        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem();
        using MaterialCompiler compiler = new(host.RenderingSystem);
        TestMaterialAsset asset = new() { Name = "a" };

        Assert.Multiple(() =>
        {
            Assert.That(compiler.Accepts(asset, "ghost"), Is.False);
            Assert.That(compiler.TryGet(asset, "ghost"), Is.Null);
            Assert.That(() => compiler.Get(asset, "ghost"), Throws.ArgumentException);
        });
    }

    [Test]
    public void AcceptsRoutesRejectedAssetsBeforeCompiling()
    {
        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem();
        using MaterialCompiler compiler = new(host.RenderingSystem);
        compiler.RegisterPass(new RejectingPass("off", host.RenderingSystem));
        TestMaterialAsset asset = new() { Name = "a" };

        Assert.Multiple(() =>
        {
            Assert.That(compiler.Accepts(asset, "off"), Is.False);
            Assert.That(compiler.TryGet(asset, "off"), Is.Null, "A rejecting pass yields no material.");
            Assert.That(() => compiler.Get(asset, "off"), Throws.TypeOf<InvalidDataException>(),
                "Getting a rejecting pass directly is a usage error.");
        });
    }

    [Test]
    public void ForeignFamilyAssetsNeverReachThePass()
    {
        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem();
        using MaterialCompiler compiler = new(host.RenderingSystem);
        // The pass is typed to TestMaterialAsset; the checked cast of
        // IMaterialPass<TAsset> turns foreign-family assets away at Accepts.
        compiler.RegisterPass(new StubPass("main", "test_lit_template", host.RenderingSystem));
        MaterialAsset foreign = new() { Name = "foreign" };

        Assert.Multiple(() =>
        {
            Assert.That(compiler.Accepts(foreign, "main"), Is.False);
            Assert.That(compiler.TryGet(foreign, "main"), Is.Null);
            Assert.That(() => compiler.Get(foreign, "main"), Throws.TypeOf<InvalidDataException>());
        });
    }

    [Test]
    public void GetCompilesCachesAndInvalidates()
    {
        using DummyRenderingSystemHost host = CreateRenderingSystem();
        using MaterialCompiler compiler = new(host.RenderingSystem);
        compiler.RegisterPass(new StubPass("main", "test_lit_template", host.RenderingSystem));

        TestMaterialAsset asset = new()
        {
            Name = "a",
            Surface = host.RenderingSystem.ShaderSystem.GetLibrary("test_compiler_surface"),
        };
        GraphicsMaterial material = compiler.Get(asset, "main");

        Assert.Multiple(() =>
        {
            Assert.That(compiler.Get(asset, "main"), Is.SameAs(material), "Materials cache per (asset, pass).");
            Assert.That(material.TryGetResourceId("_camera", out _), Is.True, "The template's bindings survive.");
            Assert.That(material.TryGetResourceId("_albedoTexture", out _), Is.True, "The surface's texture binds by name.");
            Assert.That(material.TryGetResourceId("_surfaceParams", out _), Is.True, "The parameter block binds by name.");
        });

        compiler.Invalidate(asset);
        Assert.That(compiler.Get(asset, "main"), Is.Not.SameAs(material),
            "Invalidation drops the compiled material; the next request compiles a fresh one.");
    }

    [Test]
    public void TextureSlotsAndParametersValidateAgainstTheSurface()
    {
        using DummyRenderingSystemHost host = CreateRenderingSystem();
        using MaterialCompiler compiler = new(host.RenderingSystem);
        compiler.RegisterPass(new StubPass("main", "test_lit_template", host.RenderingSystem));

        // Declared slot and parameter pass validation.
        ShaderLibrary surface = host.RenderingSystem.ShaderSystem.GetLibrary("test_compiler_surface");
        TestMaterialAsset valid = new()
        {
            Name = "valid",
            Surface = surface,
            Textures = new Dictionary<string, Texture2D> { ["albedoTexture"] = host.RenderingSystem.TextureWhite },
            Parameters = new Dictionary<string, Vector4> { ["pulseSpeed"] = new Vector4(2.0f, 0.0f, 0.0f, 0.0f) },
        };
        Assert.That(() => compiler.Get(valid, "main"), Throws.Nothing);

        // An undeclared slot / parameter name is a typo in the asset: fail at
        // compile time, not later at bind time.
        TestMaterialAsset typoSlot = new()
        {
            Name = "typoSlot",
            Surface = surface,
            Textures = new Dictionary<string, Texture2D> { ["albedo"] = host.RenderingSystem.TextureWhite },
        };
        TestMaterialAsset typoParam = new()
        {
            Name = "typoParam",
            Surface = surface,
            Parameters = new Dictionary<string, Vector4> { ["nonsense"] = new Vector4(1.0f, 0.0f, 0.0f, 0.0f) },
        };
        Assert.Multiple(() =>
        {
            Assert.That(() => compiler.Get(typoSlot, "main"), Throws.TypeOf<InvalidDataException>());
            Assert.That(() => compiler.Get(typoParam, "main"), Throws.TypeOf<InvalidDataException>());
        });
    }

    [Test]
    public void FallbackTexturesFollowTheAssetPolicy()
    {
        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem();
        using MaterialCompiler compiler = new(host.RenderingSystem);
        RenderingSystem rendering = host.RenderingSystem;

        MaterialAsset plain = new() { Name = "plain" };
        TestMaterialAsset family = new() { Name = "family" };

        Assert.Multiple(() =>
        {
            // The base policy is always white.
            Assert.That(compiler.ResolveFallbackTexture(plain, "_albedoTexture"), Is.SameAs(rendering.TextureWhite));
            // The family asset's own policy, addressed by slot (the leading
            // underscore of the shader resource name is stripped).
            Assert.That(compiler.ResolveFallbackTexture(family, "_albedoTexture"), Is.SameAs(rendering.TextureWhite));
            Assert.That(compiler.ResolveFallbackTexture(family, "normalTexture"), Is.SameAs(rendering.TextureFlatNormal));
            Assert.That(compiler.ResolveFallbackTexture(family, "_emissiveTexture"), Is.SameAs(rendering.TextureBlack));
        });
    }

    [Test]
    public void SurfaceRequiresASurfaceOrADefault()
    {
        using DummyRenderingSystemHost host = CreateRenderingSystem();
        ShaderLibrary surface = host.RenderingSystem.ShaderSystem.GetLibrary("test_compiler_surface");
        using MaterialCompiler noDefault = new(host.RenderingSystem);
        using MaterialCompiler withDefault = new(host.RenderingSystem, surface);
        MaterialAsset unnamed = new() { Name = "unnamed" };
        MaterialAsset named = new() { Name = "named", Surface = surface };

        Assert.Multiple(() =>
        {
            Assert.That(() => noDefault.SurfaceOf(unnamed), Throws.TypeOf<InvalidDataException>(),
                "No asset surface and no compiler default is an authoring error.");
            Assert.That(() => noDefault.SurfaceOf(null), Throws.TypeOf<InvalidDataException>());
            Assert.That(noDefault.SurfaceOf(named), Is.SameAs(surface));
            Assert.That(withDefault.SurfaceOf(unnamed), Is.SameAs(surface),
                "The compiler's default surface composes when the asset names none.");
            Assert.That(withDefault.SurfaceOf(null), Is.SameAs(surface));
        });
    }
}
