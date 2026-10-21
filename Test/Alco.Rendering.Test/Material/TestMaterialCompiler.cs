#nullable enable

using System.Numerics;
using System.Text;
using Alco.Graphics;
using NUnit.Framework;

namespace Alco.Rendering.Test;

// ─────────────────────────────────────────────────────────────────────────────
// MaterialCompiler tests: the pipeline-agnostic factory — (asset, template)
// compilation, texture-slot/parameter validation against the composed
// reflection, the default-surface rule and the asset-driven fallback texture
// policy. Runs on the NoGPU device with in-memory slang modules, mirroring
// MaterialCompilerComposeTest.
// ─────────────────────────────────────────────────────────────────────────────
/// <summary>Verifies shared compiler ownership, surface selection and material compilation.</summary>
[TestFixture]
public class TestMaterialCompiler
{
    /// <summary>A pipeline-family asset for the tests below.</summary>
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

    /// <summary>The compile factory of the tests below: a minimal materializing one.</summary>
    private static GraphicsMaterial CreateMaterial(RenderingSystem rendering, MaterialAsset asset, Shader shader)
        => rendering.CreateGraphicsMaterial(shader, $"{asset.Name}_test");

    /// <summary>The tests' compile entry point.</summary>
    private static GraphicsMaterial Compile(MaterialCompiler compiler, RenderingSystem rendering, MaterialAsset asset)
        => compiler.Compile(asset, rendering.ShaderSystem.GetLibrary("test_lit_template"),
            (a, shader) => CreateMaterial(rendering, a, shader));

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

        cbuffer camera : register(b0, space0)
        {
            float4x4 viewProjection;
        }

        cbuffer draw : register(b0, space1)
        {
            RWStructuredBuffer<float4> instances;
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
            output.position = mul(viewProjection, float4(position + instances[0].xyz, 1.0));
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

    private const string ComputeTemplate = """
        #language slang 2025
        module test_compute_template;
        import test_compiler_contract;
        cbuffer camera : register(b0, space0) { float4x4 viewProjection; }
        cbuffer draw : register(b0, space1) { RWStructuredBuffer<float4> output; }
        [shader("compute")][numthreads(1, 1, 1)]
        void MainCS<T : ISurface>()
        {
            T surface = T();
            SurfaceInput input;
            input.uv = float2(0, 0);
            output[0] = surface.GetBaseColor(input) * viewProjection[0][0];
        }
        """;

    private const string Surface = """
        #language slang 2025
        module test_compiler_surface;

        import test_compiler_contract;

        cbuffer material : register(b0, space2)
        {
            Texture2D<float4> albedoTexture;
            SamplerState linearClamp;
        }

        [MaterialParams]
        cbuffer surfaceParams : register(b1, space2)
        {
            float pulseSpeed;
        }

        public struct Surface : ISurface
        {
            public override float4 GetBaseColor(SurfaceInput input)
            {
                return albedoTexture.Sample(linearClamp, input.uv) * pulseSpeed;
            }
        }
        """;

    private static readonly Dictionary<string, string> Files = new()
    {
        ["test-compiler-contract.slang"] = Contract,
        ["test-lit-template.slang"] = LitTemplate,
        ["test-compute-template.slang"] = ComputeTemplate,
        ["test-compiler-surface.slang"] = Surface,
        ["test-compiler-alternate-surface.slang"] = Surface
            .Replace("test_compiler_surface", "test_compiler_alternate_surface")
            .Replace("surfaceParams", "alternateParams")
            .Replace("pulseSpeed", "alternateSpeed"),
        ["test-compute-surface.slang"] = Surface
            .Replace("test_compiler_surface", "test_compute_surface")
            .Replace("albedoTexture.Sample(linearClamp, input.uv)",
                "albedoTexture.SampleLevel(linearClamp, input.uv, 0)"),
    };

    /// <summary>A rendering system whose shader modules resolve from <see cref="Files"/>.</summary>
    private static DummyRenderingSystemHost CreateRenderingSystem()
    {
        return Utility.CreateRenderingSystem(resolver: ShaderModuleResolver.Create(
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
    }

    /// <summary>Shares cached compositions and owns their lifetime without owning compiled materials.</summary>
    [Test]
    public void RenderingSystem_OwnsSharedCompilerAndComposedShaders()
    {
        using DummyRenderingSystemHost host = CreateRenderingSystem();
        RenderingSystem rendering = host.RenderingSystem;
        MaterialCompiler compiler = rendering.MaterialCompiler;
        Assert.That(compiler, Is.SameAs(rendering.MaterialCompiler));
        Assert.That(rendering.ShaderSystem.Modules.GetLoadedModuleNames(), Is.Empty,
            "Creating the shared compiler must not eagerly load a pipeline-family surface.");
        ShaderLibrary template = rendering.ShaderSystem.GetLibrary("test_lit_template");
        ShaderLibrary surface = rendering.ShaderSystem.GetLibrary("test_compiler_surface");
        MaterialAsset asset = new() { Surface = surface };
        using GraphicsMaterial material = Compile(compiler, rendering, asset);
        Shader shader = compiler.ComposeGraphics(template, surface);
        Assert.That(shader, Is.SameAs(rendering.MaterialCompiler.ComposeGraphics(template, surface)));
        Assert.That(material.Shader, Is.SameAs(shader));

        host.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(compiler.IsDisposed, Is.True);
            Assert.That(shader.IsDisposed, Is.True);
            Assert.That(material.IsDisposed, Is.False, "Compiled materials remain caller-owned.");
            Assert.That(() => compiler.ComposeGraphics(template, surface), Throws.TypeOf<ObjectDisposedException>());
        });
    }

    /// <summary>Subscribes the shared compiler to the rendering shader system's module invalidation.</summary>
    [Test]
    public void RenderingSystem_SharedCompilerObservesModuleInvalidation()
    {
        using DummyRenderingSystemHost host = CreateRenderingSystem();
        RenderingSystem rendering = host.RenderingSystem;
        MaterialCompiler compiler = rendering.MaterialCompiler;
        ShaderLibrary template = rendering.ShaderSystem.GetLibrary("test_lit_template");
        ShaderLibrary surface = rendering.ShaderSystem.GetLibrary("test_compiler_surface");
        Shader shader = compiler.ComposeGraphics(template, surface);
        _ = shader.GetShaderModules();
        uint version = shader.Version;
        List<Shader> invalidated = [];
        compiler.ShaderInvalidated += invalidated.Add;
        string dependency = rendering.ShaderSystem.Modules.GetModuleDependencies(template.Name)
            .First(path => path.Contains("contract", StringComparison.OrdinalIgnoreCase));

        rendering.ShaderSystem.InvalidateModulesContaining(dependency);

        Assert.Multiple(() =>
        {
            Assert.That(invalidated, Is.EqualTo(new[] { shader }));
            Assert.That(shader.Version, Is.GreaterThan(version));
            Assert.That(compiler.ComposeGraphics(template, surface), Is.SameAs(shader));
            Assert.That(shader.GetShaderModules().IsGraphicsShader, Is.True);
        });
    }

    /// <summary>Uses per-call fallbacks consistently without changing the material's authored surface.</summary>
    [Test]
    public void Compile_PerCallDefaultSurface_SelectsAndPacksEffectiveSurface()
    {
        using DummyRenderingSystemHost host = CreateRenderingSystem();
        RenderingSystem rendering = host.RenderingSystem;
        MaterialCompiler compiler = rendering.MaterialCompiler;
        ShaderLibrary template = rendering.ShaderSystem.GetLibrary("test_lit_template");
        ShaderLibrary surface = rendering.ShaderSystem.GetLibrary("test_compiler_surface");
        ShaderLibrary alternate = rendering.ShaderSystem.GetLibrary("test_compiler_alternate_surface");
        MaterialAsset asset = new()
        {
            Name = "per_call_default",
            Textures = new Dictionary<string, Texture2D> { ["albedoTexture"] = rendering.TextureWhite },
        };
        using GraphicsMaterial first = compiler.Compile(asset, template, null,
            (a, shader) => CreateMaterial(rendering, a, shader), defaultSurface: surface);
        using GraphicsMaterial second = compiler.Compile(asset, template, null,
            (a, shader) => CreateMaterial(rendering, a, shader), defaultSurface: alternate);
        using GraphicsMaterial repeated = compiler.Compile(asset, template, null,
            (a, shader) => CreateMaterial(rendering, a, shader), defaultSurface: alternate);

        Assert.Multiple(() =>
        {
            Assert.That(asset.Surface, Is.Null);
            Assert.That(first.Shader, Is.SameAs(compiler.ComposeGraphics(template, surface)));
            Assert.That(second.Shader, Is.SameAs(compiler.ComposeGraphics(template, alternate)));
            Assert.That(first.Parameters.GetBuffer("surfaceParams"), Is.Not.Null);
            Assert.That(second.Parameters.GetBuffer("alternateParams"), Is.Not.Null);
            Assert.That(second.Parameters.GetBuffer("alternateParams"),
                Is.SameAs(repeated.Parameters.GetBuffer("alternateParams")),
                "The parameter cache must be keyed by the effective surface.");
            Assert.That(() => compiler.SurfaceOf(asset), Throws.TypeOf<InvalidDataException>(),
                "A per-call fallback must not become the shared compiler's default.");
        });

        asset.Surface = surface;
        asset.Parameters = new Dictionary<string, ShaderValue> { ["pulseSpeed"] = 2.0f };
        using GraphicsMaterial explicitSurface = compiler.Compile(asset, template, null,
            (a, shader) => CreateMaterial(rendering, a, shader), defaultSurface: alternate);
        Assert.That(explicitSurface.Shader, Is.SameAs(first.Shader), "The asset's explicit surface wins.");
        Assert.That(asset.Surface, Is.SameAs(surface));
    }

    /// <summary>Lets a per-call fallback override only the compiler's default, not the asset's surface.</summary>
    [Test]
    public void Compile_PerCallDefaultSurface_TakesPrecedenceOverCompilerDefault()
    {
        using DummyRenderingSystemHost host = CreateRenderingSystem();
        RenderingSystem rendering = host.RenderingSystem;
        ShaderLibrary template = rendering.ShaderSystem.GetLibrary("test_lit_template");
        ShaderLibrary surface = rendering.ShaderSystem.GetLibrary("test_compiler_surface");
        ShaderLibrary alternate = rendering.ShaderSystem.GetLibrary("test_compiler_alternate_surface");
        using MaterialCompiler compiler = new(rendering, alternate);
        MaterialAsset asset = new()
        {
            Parameters = new Dictionary<string, ShaderValue> { ["pulseSpeed"] = 3.0f },
        };
        using GraphicsMaterial material = compiler.Compile(asset, template, null,
            (a, shader) => CreateMaterial(rendering, a, shader), defaultSurface: surface);

        Assert.Multiple(() =>
        {
            Assert.That(material.Shader, Is.SameAs(compiler.ComposeGraphics(template, surface)));
            Assert.That(material.Parameters.GetBuffer("surfaceParams"), Is.Not.Null);
            Assert.That(compiler.SurfaceOf(asset), Is.SameAs(alternate), "SurfaceOf keeps its existing default semantics.");
            Assert.That(asset.Surface, Is.Null);
        });
    }

    /// <summary>Retains compiler-default surface selection and parameter packing for compute materials.</summary>
    [Test]
    public void CompileCompute_UsesCompilerDefaultSurfaceForParameters()
    {
        using DummyRenderingSystemHost host = CreateRenderingSystem();
        RenderingSystem rendering = host.RenderingSystem;
        ShaderLibrary template = rendering.ShaderSystem.GetLibrary("test_compute_template");
        ShaderLibrary surface = rendering.ShaderSystem.GetLibrary("test_compute_surface");
        using MaterialCompiler compiler = new(rendering, surface);
        MaterialAsset asset = new()
        {
            Parameters = new Dictionary<string, ShaderValue> { ["pulseSpeed"] = 4.0f },
        };
        using ComputeMaterial material = compiler.CompileCompute(asset, template);

        Assert.Multiple(() =>
        {
            Assert.That(material.ParameterSet.GetBuffer("surfaceParams"), Is.Not.Null);
            Assert.That(asset.Surface, Is.Null);
        });
    }

    /// <summary>Produces fresh caller-owned materials from the rendering system's shared compiler.</summary>
    [Test]
    public void CompileProducesFreshCallerOwnedMaterials()
    {
        using DummyRenderingSystemHost host = CreateRenderingSystem();
        MaterialCompiler compiler = host.RenderingSystem.MaterialCompiler;

        TestMaterialAsset asset = new()
        {
            Name = "a",
            Surface = host.RenderingSystem.ShaderSystem.GetLibrary("test_compiler_surface"),
        };
        GraphicsMaterial material = Compile(compiler, host.RenderingSystem, asset);

        Assert.Multiple(() =>
        {
            Assert.That(Compile(compiler, host.RenderingSystem, asset), Is.Not.SameAs(material),
                "Every compile produces a fresh caller-owned material; sharing is the caller's job.");
            Assert.That(material.TryGetResourceId("camera", out _), Is.True, "The template's bindings survive.");
            Assert.That(material.TryGetResourceId("albedoTexture", out _), Is.True, "The surface's texture binds by name.");
            Assert.That(material.TryGetResourceId("surfaceParams", out _), Is.True, "The parameter block binds by name.");
        });
    }

    /// <summary>Rejects texture slots and parameter names absent from the selected surface.</summary>
    [Test]
    public void TextureSlotsAndParametersValidateAgainstTheSurface()
    {
        using DummyRenderingSystemHost host = CreateRenderingSystem();
        using MaterialCompiler compiler = new(host.RenderingSystem);

        // Declared slot and parameter pass validation.
        ShaderLibrary surface = host.RenderingSystem.ShaderSystem.GetLibrary("test_compiler_surface");
        TestMaterialAsset valid = new()
        {
            Name = "valid",
            Surface = surface,
            Textures = new Dictionary<string, Texture2D> { ["albedoTexture"] = host.RenderingSystem.TextureWhite },
            Parameters = new Dictionary<string, ShaderValue> { ["pulseSpeed"] = new Vector4(2.0f, 0.0f, 0.0f, 0.0f) },
        };
        Assert.That(() => Compile(compiler, host.RenderingSystem, valid), Throws.Nothing);

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
            Parameters = new Dictionary<string, ShaderValue> { ["nonsense"] = new Vector4(1.0f, 0.0f, 0.0f, 0.0f) },
        };
        Assert.Multiple(() =>
        {
            Assert.That(() => Compile(compiler, host.RenderingSystem, typoSlot), Throws.TypeOf<InvalidDataException>());
            Assert.That(() => Compile(compiler, host.RenderingSystem, typoParam), Throws.TypeOf<InvalidDataException>());
        });
    }

    /// <summary>Applies the material asset's texture fallback policy during compilation.</summary>
    [Test]
    public void FallbackTexturesFollowTheAssetPolicy()
    {
        using DummyRenderingSystemHost host = CreateRenderingSystem();
        using MaterialCompiler compiler = new(host.RenderingSystem);
        RenderingSystem rendering = host.RenderingSystem;

        // Unbound slots bind the asset's own fallback policy (base: always
        // white), addressed through a real compile of the test surface, which
        // declares the albedo slot.
        MaterialAsset plain = new()
        {
            Name = "plain",
            Surface = rendering.ShaderSystem.GetLibrary("test_compiler_surface"),
        };
        GraphicsMaterial plainMaterial = Compile(compiler, rendering, plain);
        Assert.That(plainMaterial.Parameters.GetTexture("albedoTexture"), Is.SameAs(rendering.TextureWhite),
            "The base policy is always white.");

        // The family asset's own policy decides per slot name.
        TestMaterialAsset family = new()
        {
            Name = "family",
            Surface = rendering.ShaderSystem.GetLibrary("test_compiler_surface"),
        };
        GraphicsMaterial familyMaterial = Compile(compiler, rendering, family);
        Assert.That(familyMaterial.Parameters.GetTexture("albedoTexture"), Is.SameAs(rendering.TextureWhite),
            "The family asset keeps white for a non-matching slot prefix.");
    }

    /// <summary>Requires an explicit surface on the shared compiler while preserving family defaults.</summary>
    [Test]
    public void SurfaceRequiresASurfaceOrADefault()
    {
        using DummyRenderingSystemHost host = CreateRenderingSystem();
        ShaderLibrary surface = host.RenderingSystem.ShaderSystem.GetLibrary("test_compiler_surface");
        MaterialCompiler noDefault = host.RenderingSystem.MaterialCompiler;
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
