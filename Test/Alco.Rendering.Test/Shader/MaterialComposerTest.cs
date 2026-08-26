using System.Numerics;
using Alco.Graphics;
using Alco.ShaderCompiler;
#nullable enable

using NUnit.Framework;

namespace Alco.Rendering.Test;

// ─────────────────────────────────────────────────────────────────────────────
// MaterialComposer tests: template×surface composition through slang's component
// system (no generated wrappers), value specialization as the define
// replacement, module-level parameter-block reflection, parameter packing and
// hot-reload invalidation. Runs on the NoGPU device; only module/reflection
// level behavior is asserted (pipelines need a real device). The in-memory
// modules mirror the shipped convention: surface resources live in set-scoped
// cbuffer blocks, parameter blocks are discovered by the [MaterialParams] marker
// and mix scalar/vector float members. Templates and surfaces are addressed by
// ShaderLibrary references, as production passes and assets do.
// ─────────────────────────────────────────────────────────────────────────────
[TestFixture]
public class MaterialComposerTest
{
    private const string Contract = """
        #language slang 2025
        module test_contract;

        public struct SurfaceInput
        {
            public float2 uv;
            public float4 tint;
        }

        // Marks a cbuffer as a material-parameter block (the engine discovers
        // parameter blocks by this marker, not by name).
        [__AttributeUsage(_AttributeTargets.Var)]
        public struct MaterialParams {};

        public interface IVertexSurface
        {
            void ModifyVertex(inout float3 worldPos, float2 uv) { }
        }

        public interface IAlbedoSurface
        {
            float4 GetBaseColor(SurfaceInput input) { return input.tint; }
        }

        public interface ISurface : IVertexSurface, IAlbedoSurface { }
        """;

    private const string LitTemplate = """
        #language slang 2025
        module test_lit_template;

        import test_contract;

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
            T surface = T();
            float3 worldPos = position + _instances[0].xyz;
            surface.ModifyVertex(worldPos, uv);
            LitV2F output;
            output.position = mul(viewProjection, float4(worldPos, 1.0));
            output.uv = uv;
            return output;
        }

        [shader("fragment")]
        public float4 MainPS<T : ISurface>(LitV2F input) : SV_TARGET
        {
            T surface = T();
            SurfaceInput surfaceInput;
            surfaceInput.uv = input.uv;
            surfaceInput.tint = float4(0.5, 0.5, 0.5, 1.0);
            return surface.GetBaseColor(surfaceInput);
        }
        """;

    private const string ShadowTemplate = """
        #language slang 2025
        module test_shadow_template;

        import test_contract;

        cbuffer _data : register(b0, space0)
        {
            float4x4 lightViewProjection;
        }

        cbuffer _draw : register(b0, space1)
        {
            RWStructuredBuffer<float4> _instances;
        }

        public struct ShadowV2F
        {
            public float4 position : SV_POSITION;
            public float2 uv : TEXCOORD0;
        }

        [shader("vertex")]
        public ShadowV2F MainVS<T : ISurface>(float3 position : POSITION, float2 uv : TEXCOORD0)
        {
            T surface = T();
            float3 worldPos = position + _instances[0].xyz;
            surface.ModifyVertex(worldPos, uv);
            ShadowV2F output;
            output.position = mul(lightViewProjection, float4(worldPos, 1.0));
            output.uv = uv;
            return output;
        }

        [shader("fragment")]
        public void MainPS<T : ISurface, let AlphaTest : bool>(ShadowV2F input)
        {
            if (AlphaTest)
            {
                T surface = T();
                SurfaceInput surfaceInput;
                surfaceInput.uv = input.uv;
                surfaceInput.tint = float4(1, 1, 1, 1);
                if (surface.GetBaseColor(surfaceInput).a < 0.5)
                    discard;
            }
        }
        """;

    private const string FeedTemplate = """
        #language slang 2025
        module test_feed_template;

        import test_contract;

        cbuffer _pass : register(b0, space0)
        {
            [[vk::image_format("rgba16f")]] RWTexture2D<float4> _output;
        }

        cbuffer _draw : register(b0, space1)
        {
            RWStructuredBuffer<uint> _tiles;
        }

        [shader("compute")]
        [numthreads(8, 8, 1)]
        public void MainCS<T : ISurface>(uint2 tid : SV_DispatchThreadID)
        {
            T surface = T();
            SurfaceInput surfaceInput;
            surfaceInput.uv = float2(0, 0);
            surfaceInput.tint = float4(1, 1, 1, 1);
            _output[tid] = surface.GetBaseColor(surfaceInput);
            _tiles[tid.x] = 0u;
        }
        """;

    private const string Surface = """
        #language slang 2025
        module test_surface;

        import test_contract;

        cbuffer _material : register(b0, space2)
        {
            Texture2D<float4> _albedoTexture;
            SamplerState _albedoTextureSampler;
        }

        [MaterialParams]
        cbuffer _surfaceParams : register(b1, space2)
        {
            float pulseSpeed;
            float3 pulseColor;
            float bandFrequency;
        }

        public struct Surface : ISurface
        {
            public override float4 GetBaseColor(SurfaceInput input)
            {
                return _albedoTexture.Sample(_albedoTextureSampler, input.uv)
                     * input.tint * pulseSpeed;
            }
        }
        """;

    private const string MinimalSurface = """
        #language slang 2025
        module test_surface_minimal;

        import test_contract;

        public struct Surface : ISurface { }
        """;

    private const string MultiBlockSurface = """
        #language slang 2025
        module test_surface_multiblock;

        import test_contract;

        [MaterialParams]
        cbuffer _pulse : register(b0, space2)
        {
            float pulseSpeed;
            float3 pulseColor;
        }

        [MaterialParams]
        cbuffer _bands : register(b1, space2)
        {
            float bandFrequency;
        }

        public struct Surface : ISurface
        {
            public override float4 GetBaseColor(SurfaceInput input)
            {
                return input.tint * pulseSpeed * bandFrequency;
            }
        }
        """;

    private static readonly Dictionary<string, string> Files = new()
    {
        ["test-contract.slang"] = Contract,
        ["test-lit-template.slang"] = LitTemplate,
        ["test-shadow-template.slang"] = ShadowTemplate,
        ["test-feed-template.slang"] = FeedTemplate,
        ["test-surface.slang"] = Surface,
        ["test-surface-minimal.slang"] = MinimalSurface,
        ["test-surface-multiblock.slang"] = MultiBlockSurface,
    };

    private static MaterialComposer CreateComposer(DummyRenderingSystemHost host, out ShaderSystem shaderSystem)
    {
        shaderSystem = new ShaderSystem(host.RenderingSystem, new SlangCompilerOptions
        {
            Resolver = path =>
            {
                // Imports probe several module-name→file forms; match on the dashed form.
                string key = SlangPathUtility.NormalizePath(path).Replace('/', '-').Replace('_', '-');
                foreach (KeyValuePair<string, string> file in Files)
                {
                    if (key.EndsWith(file.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        return file.Value;
                    }
                }
                return null;
            },
        }, cacheDirectory: null);
        return new MaterialComposer(host.RenderingSystem, shaderSystem);
    }

    [Test]
    public void ComposeGraphics_ComposesTemplateWithSurface_AndCaches()
    {
        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem();
        using MaterialComposer composer = CreateComposer(host, out ShaderSystem shaderSystem);
        using (shaderSystem)
        {
            ShaderLibrary lit = shaderSystem.GetLibrary("test_lit_template");
            ShaderLibrary surface = shaderSystem.GetLibrary("test_surface");
            Shader shader = composer.ComposeGraphics(lit, surface);
            ShaderModulesInfo modules = shader.GetShaderModules();

            Assert.Multiple(() =>
            {
                Assert.That(shader.Name, Is.EqualTo("test_lit_template+test_surface"));
                Assert.That(modules.IsComputeShader, Is.False);
                Assert.That(modules.IsGraphicsShader, Is.True);
                Assert.That(modules.VertexShader!.Value.Source.Length, Is.GreaterThan(4));
                Assert.That(modules.FragmentShader!.Value.Source.Length, Is.GreaterThan(4));
                Assert.That(modules.ReflectionInfo.TryGetResourceId("_albedoTexture", out _), Is.True);
                Assert.That(modules.ReflectionInfo.TryGetResourceId("_surfaceParams", out _), Is.True);
                Assert.That(composer.ComposeGraphics(lit, surface),
                    Is.SameAs(shader), "same composition must return the cached shader");
            });
        }
    }

    [Test]
    public void ComposeGraphics_MinimalSurface_RidesInterfaceDefaults()
    {
        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem();
        using MaterialComposer composer = CreateComposer(host, out ShaderSystem shaderSystem);
        using (shaderSystem)
        {
            Shader shader = composer.ComposeGraphics(
                shaderSystem.GetLibrary("test_lit_template"),
                shaderSystem.GetLibrary("test_surface_minimal"));
            ShaderModulesInfo modules = shader.GetShaderModules();

            Assert.Multiple(() =>
            {
                Assert.That(modules.IsGraphicsShader, Is.True);
                Assert.That(modules.ReflectionInfo.TryGetResourceId("_albedoTexture", out _), Is.False,
                    "the defaults reference no texture");
            });
        }
    }

    [Test]
    public void ComposeCompute_ComposesSingleComputeEntry()
    {
        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem();
        using MaterialComposer composer = CreateComposer(host, out ShaderSystem shaderSystem);
        using (shaderSystem)
        {
            Shader shader = composer.ComposeCompute(
                shaderSystem.GetLibrary("test_feed_template"),
                shaderSystem.GetLibrary("test_surface"));
            ShaderModulesInfo modules = shader.GetShaderModules();

            Assert.Multiple(() =>
            {
                Assert.That(modules.IsComputeShader, Is.True);
                Assert.That(modules.ComputeShader!.Value.WorkgroupSize, Is.EqualTo((8u, 8u, 1u)));
                Assert.That(modules.ReflectionInfo.TryGetResourceId("_output", out _), Is.True);
                Assert.That(modules.ReflectionInfo.TryGetResourceId("_albedoTexture", out _), Is.True);
            });
        }
    }

    [Test]
    public void Compose_StageMixMismatch_Throws()
    {
        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem();
        using MaterialComposer composer = CreateComposer(host, out ShaderSystem shaderSystem);
        using (shaderSystem)
        {
            ShaderLibrary surface = shaderSystem.GetLibrary("test_surface");
            Assert.Multiple(() =>
            {
                Assert.Throws<InvalidOperationException>(() =>
                    _ = composer.ComposeGraphics(shaderSystem.GetLibrary("test_feed_template"), surface).GetShaderModules());
                Assert.Throws<InvalidOperationException>(() =>
                    _ = composer.ComposeCompute(shaderSystem.GetLibrary("test_lit_template"), surface).GetShaderModules());
            });
        }
    }

    [Test]
    public void ComposeGraphics_ValueSpecializations_AreDistinctShaders()
    {
        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem();
        using MaterialComposer composer = CreateComposer(host, out ShaderSystem shaderSystem);
        using (shaderSystem)
        {
            ShaderLibrary shadow = shaderSystem.GetLibrary("test_shadow_template");
            ShaderLibrary surface = shaderSystem.GetLibrary("test_surface");
            Shader opaque = composer.ComposeGraphics(shadow, surface, ["false"]);
            Shader cutout = composer.ComposeGraphics(shadow, surface, ["true"]);

            Assert.Multiple(() =>
            {
                Assert.That(cutout, Is.Not.SameAs(opaque));
                Assert.That(opaque.Name, Is.EqualTo("test_shadow_template+test_surface[false]"));
                Assert.That(cutout.Name, Is.EqualTo("test_shadow_template+test_surface[true]"));
                // The specialization fold is real: the opaque PS carries no sample.
                Assert.That(
                    cutout.GetShaderModules().FragmentShader!.Value.Source.Length,
                    Is.GreaterThan(opaque.GetShaderModules().FragmentShader!.Value.Source.Length));
            });
        }
    }

    [Test]
    public void GetParamsLayouts_ReadsMarkedBlockMembers_WithoutLinking()
    {
        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem();
        using MaterialComposer composer = CreateComposer(host, out ShaderSystem shaderSystem);
        using (shaderSystem)
        {
            IReadOnlyDictionary<string, IReadOnlyList<ShaderUniformMember>> layouts =
                composer.GetParamsLayouts(shaderSystem.GetLibrary("test_surface"));

            Assert.Multiple(() =>
            {
                // Only the [MaterialParams]-marked block is reported — the unmarked
                // resource block above it is not a parameter block.
                Assert.That(layouts.Keys, Is.EqualTo(new[] { "_surfaceParams" }));
                IReadOnlyList<ShaderUniformMember> members = layouts["_surfaceParams"];
                Assert.That(members.Select(member => member.Name),
                    Is.EqualTo(new[] { "pulseSpeed", "pulseColor", "bandFrequency" }));
                Assert.That(members[0].OffsetBytes, Is.EqualTo(0u));
                Assert.That(members[1].OffsetBytes, Is.EqualTo(16u));
                Assert.That(members[2].OffsetBytes, Is.EqualTo(28u));
                Assert.That(composer.GetParamsLayouts(shaderSystem.GetLibrary("test_surface_minimal")),
                    Is.Empty, "a module without a marked block reports empty");
            });
        }
    }

    [Test]
    public void PackParamsBuffer_ValidatesNamesAndPacks()
    {
        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem();
        using MaterialComposer composer = CreateComposer(host, out ShaderSystem shaderSystem);
        using (shaderSystem)
        {
            IReadOnlyList<ShaderUniformMember> layout =
                composer.GetParamsLayouts(shaderSystem.GetLibrary("test_surface"))["_surfaceParams"];

            Assert.Multiple(() =>
            {
                Assert.Throws<InvalidDataException>(() => composer.PackParamsBuffer(
                    layout, new Dictionary<string, Vector4> { ["typo"] = new(1f, 0f, 0f, 0f) }, "mat"),
                    "an unknown parameter name must fail");
                Assert.DoesNotThrow(() =>
                {
                    // A Vector4 value reads as many leading components as the
                    // member takes; the unmentioned member reads zero.
                    using GraphicsBuffer buffer = composer.PackParamsBuffer(layout,
                        new Dictionary<string, Vector4>
                        {
                            ["pulseSpeed"] = new(2f, 0f, 0f, 0f),
                            ["pulseColor"] = new(1f, 0.5f, 0.25f, 0f),
                        }, "mat");
                });
            });
        }
    }

    [Test]
    public void PackParamsBuffers_PacksEveryMarkedBlock()
    {
        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem();
        using MaterialComposer composer = CreateComposer(host, out ShaderSystem shaderSystem);
        using (shaderSystem)
        {
            // A surface may split its parameters across several marked blocks;
            // discovery reports each one.
            IReadOnlyDictionary<string, IReadOnlyList<ShaderUniformMember>> layouts =
                composer.GetParamsLayouts(shaderSystem.GetLibrary("test_surface_multiblock"));
            Assert.That(layouts.Keys, Is.EqualTo(new[] { "_pulse", "_bands" }));

            // One value table spans blocks by member name; each marked block gets
            // its own buffer. An unknown name fails against the union of members.
            IReadOnlyDictionary<string, GraphicsBuffer> buffers = composer.PackParamsBuffers(
                layouts,
                new Dictionary<string, Vector4>
                {
                    ["pulseSpeed"] = new(2f, 0f, 0f, 0f),
                    ["pulseColor"] = new(1f, 0.5f, 0.25f, 0f),
                    ["bandFrequency"] = new(4f, 0f, 0f, 0f),
                }, "mat");
            using (buffers["_pulse"])
            using (buffers["_bands"])
            {
                Assert.Multiple(() =>
                {
                    Assert.That(buffers.Keys, Is.EquivalentTo(layouts.Keys));
                    Assert.Throws<InvalidDataException>(() => composer.PackParamsBuffers(
                        layouts, new Dictionary<string, Vector4> { ["typo"] = new(1f, 0f, 0f, 0f) }, "mat"));
                });
            }
        }
    }

    [Test]
    public void EnumerateTextureSlots_ListsTexturesOfTheSurfaceModule()
    {
        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem();
        using MaterialComposer composer = CreateComposer(host, out ShaderSystem shaderSystem);
        using (shaderSystem)
        {
            // Slot discovery reads the surface module's own declarations —
            // set-number-free (a ParameterBlock's set is compiler-assigned).
            Assert.That(composer.EnumerateTextureSlots(shaderSystem.GetLibrary("test_surface")),
                Is.EqualTo(new[] { "_albedoTexture" }));
        }
    }

    [Test]
    public void ModuleInvalidation_ReloadsComposedShader_AndClearsParamsLayout()
    {
        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem();
        using MaterialComposer composer = CreateComposer(host, out ShaderSystem shaderSystem);
        using (shaderSystem)
        {
            ShaderLibrary surface = shaderSystem.GetLibrary("test_surface");
            Shader shader = composer.ComposeGraphics(shaderSystem.GetLibrary("test_lit_template"), surface);
            // Shaders compile lazily — pull the modules once so the dependency
            // graph exists for the invalidation below.
            _ = shader.GetShaderModules();
            Assert.That(composer.GetParamsLayouts(surface), Is.Not.Empty);
            uint versionBefore = shader.Version;
            List<Shader> invalidated = [];
            composer.ShaderInvalidated += invalidated.Add;

            // Both template and surface import the contract; touching it must
            // reload the composed shader and drop the stale param layout.
            string contractDep = shaderSystem.Modules.GetModuleDependencies("test_lit_template")
                .First(dep => dep.Contains("contract", StringComparison.OrdinalIgnoreCase));
            shaderSystem.Modules.InvalidateModulesContaining(contractDep);

            Assert.Multiple(() =>
            {
                Assert.That(invalidated, Is.EqualTo(new[] { shader }));
                Assert.That(shader.Version, Is.GreaterThan(versionBefore));
                Assert.DoesNotThrow(() => _ = shader.GetShaderModules());
                // The layout was recomputed from the rebuilt session.
                Assert.That(composer.GetParamsLayouts(surface), Is.Not.Empty);
            });
        }
    }
}
