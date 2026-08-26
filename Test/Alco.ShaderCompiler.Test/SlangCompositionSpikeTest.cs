using Alco.Graphics;
using NUnit.Framework;

namespace Alco.ShaderCompiler;

// ─────────────────────────────────────────────────────────────────────────────
// Phase-0 spike for the material-composition refactor: proves the slang
// mechanisms the rebuilt MaterialCompiler stands on, in the shape the material
// system uses them (contract + pass template + surface):
//
//   1. cross-module composite + type specialization — the template module owns
//      the generic [shader] entry points, the surface module owns the Surface
//      type, and NO generated wrapper module is involved;
//   2. interface default implementations + interface inheritance — a surface
//      overrides only what it customizes (the "unconnected pin" equivalent);
//   3. bool value specialization (<let AlphaTest : bool>) replacing pass-private
//      defines (SHADOW_CUTOUT), with dead-code elimination verified through
//      reflection (the gated texture disappears when AlphaTest is false);
//   4. module-level reflection of a _materialParams block without entry points
//      or a link — the parameter probe no longer borrows a pass template;
//   5. composed-program disk-cache round-trip.
// ─────────────────────────────────────────────────────────────────────────────
[TestFixture]
public class SlangCompositionSpikeTest
{
    // The surface contract: granular capability interfaces with defaults,
    // aggregated by ISurface — mirrors Shaders/Libs/AlcoWorld3D_Surface.slang.
    private const string Contract = """
        #language slang 2025
        module test_contract;

        public struct SurfaceInput
        {
            public float2 uv;
            public float4 tint;
        }

        public interface IVertexSurface
        {
            void ModifyVertex(inout float3 worldPos, float2 uv) { }
        }

        public interface IAlbedoSurface
        {
            float4 GetBaseColor(SurfaceInput input) { return input.tint; }
        }

        public interface IEmissiveSurface
        {
            float3 GetEmissive(SurfaceInput input) { return float3(0, 0, 0); }
        }

        public interface ISurface : IVertexSurface, IAlbedoSurface, IEmissiveSurface { }
        """;

    // A lit pass template (gbuffer-like): the vertex stage deforms through the
    // surface, the fragment stage shades with it. The [shader] entry points
    // live in the template itself, generic over ISurface.
    private const string LitTemplate = """
        #language slang 2025
        module test_lit_template;

        import test_contract;

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
            T surface = T();
            float3 worldPos = position + instances[0].xyz;
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
            float4 baseColor = surface.GetBaseColor(surfaceInput);
            return float4(baseColor.rgb + surface.GetEmissive(surfaceInput), baseColor.a);
        }
        """;

    // A depth-only pass template (shadow-like): the fragment stage exists only
    // to alpha-test, gated by a value specialization — the SHADOW_CUTOUT
    // define's replacement.
    private const string ShadowTemplate = """
        #language slang 2025
        module test_shadow_template;

        import test_contract;

        cbuffer data : register(b0, space0)
        {
            float4x4 lightViewProjection;
        }

        cbuffer draw : register(b0, space1)
        {
            RWStructuredBuffer<float4> instances;
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
            float3 worldPos = position + instances[0].xyz;
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
                float4 baseColor = surface.GetBaseColor(surfaceInput);
                if (baseColor.a < 0.5)
                    discard;
            }
        }
        """;

    // A partially-implemented surface: albedo only, with a texture in the
    // material set and a mixed-type parameter block; emissive and vertex
    // deformation ride the interface defaults.
    private const string Surface = """
        #language slang 2025
        module test_surface;

        import test_contract;

        [[vk::binding(0, 2)]] Texture2D<float4> albedoTexture;
        [[vk::binding(1, 2)]] SamplerState albedoTextureSampler;

        [[vk::binding(2, 2)]] cbuffer materialParams
        {
            float pulseSpeed;
            float3 pulseColor;
            float bandFrequency;
        }

        public struct Surface : ISurface
        {
            public override float4 GetBaseColor(SurfaceInput input)
            {
                return albedoTexture.Sample(albedoTextureSampler, input.uv)
                     * input.tint * pulseSpeed;
            }
        }
        """;

    // The minimal material: every attribute rides the interface defaults.
    private const string MinimalSurface = """
        #language slang 2025
        module test_surface_minimal;

        import test_contract;

        public struct Surface : ISurface { }
        """;

    [Test]
    public void CrossModuleComposite_SpecializesTemplateEntriesWithSurfaceType()
    {
        using SlangModuleSystem system = new(OptionsFor(SpikeFiles()), null);

        // The companion type name fills every entry's leading type parameter;
        // the lit template's entries take no value parameters.
        using SlangProgram program = system.GetComposedProgram(
            "test_lit_template", "test_surface", []);

        Assert.Multiple(() =>
        {
            Assert.That(program.EntryPoints, Has.Count.EqualTo(2));
            Assert.That(program.EntryCode, Has.Length.EqualTo(2));
            foreach (byte[] code in program.EntryCode)
            {
                Assert.That(code[0..4], Is.EqualTo(new byte[] { 0x03, 0x02, 0x23, 0x07 }),
                    "every entry must be SPIR-V (magic 0x07230203)");
            }
            Assert.That(
                SlangCompileSession.SlangStageToEngine(program.EntryPoints[0].Stage),
                Is.EqualTo(Alco.Graphics.ShaderStage.Vertex));
            Assert.That(
                SlangCompileSession.SlangStageToEngine(program.EntryPoints[1].Stage),
                Is.EqualTo(Alco.Graphics.ShaderStage.Fragment));
            // Resources of both modules appear in the composed reflection.
            Assert.That(program.Reflection.TryGetResourceId("albedoTexture", out _), Is.True);
            Assert.That(program.Reflection.TryGetResourceId("materialParams", out _), Is.True);
        });

        using SlangProgram again = system.GetComposedProgram(
            "test_lit_template", "test_surface", []);
        Assert.That(again, Is.SameAs(program), "same composition must return the cached program");
    }

    [Test]
    public void InterfaceDefaults_SurfaceOverridesOnlyWhatItCustomizes()
    {
        using SlangModuleSystem system = new(OptionsFor(SpikeFiles()), null);

        // The minimal surface implements nothing — every attribute rides the
        // interface defaults while the lit template consumes all of them.
        using SlangProgram program = system.GetComposedProgram(
            "test_lit_template", "test_surface_minimal", []);

        Assert.Multiple(() =>
        {
            Assert.That(program.EntryCode, Has.Length.EqualTo(2));
            Assert.That(program.EntryCode[1].Length, Is.GreaterThan(4));
            Assert.That(program.Reflection.TryGetResourceId("albedoTexture", out _), Is.False,
                "the defaults reference no texture");
            Assert.That(program.Reflection.TryGetResourceId("materialParams", out _), Is.False);
        });
    }

    [Test]
    public void ValueSpecialization_ReplacesPassPrivateDefine_AndDeadStripsIt()
    {
        using SlangModuleSystem system = new(OptionsFor(SpikeFiles()), null);

        // MainVS<T>, MainPS<T, let AlphaTest : bool>: the shadow pass's alpha-test
        // axis is one value argument on the fragment entry.
        using SlangProgram opaque = system.GetComposedProgram(
            "test_shadow_template", "test_surface", ["false"]);
        using SlangProgram cutout = system.GetComposedProgram(
            "test_shadow_template", "test_surface", ["true"]);

        Assert.Multiple(() =>
        {
            Assert.That(cutout, Is.Not.SameAs(opaque), "distinct specializations compile separately");
            // Finding: unlike a preprocessor define (which removes the fetch
            // lexically), specialization folds the branch in CODE but keeps the
            // surface's explicitly-bound global resource in the program layout.
            // The binding side therefore always sees the surface's full resource
            // set and binds fallbacks for what a specialization never samples.
            Assert.That(opaque.Reflection.TryGetResourceId("albedoTexture", out _), Is.True);
            Assert.That(cutout.Reflection.TryGetResourceId("albedoTexture", out _), Is.True);
            // The fold itself is real: the opaque PS carries no sample/branch.
            Assert.That(cutout.EntryCode[1].Length, Is.GreaterThan(opaque.EntryCode[1].Length),
                "the opaque PS must be specialized down to (near-)empty code");
        });
    }

    [Test]
    public void ModuleLayout_ReportsParameterBlockMembers_WithoutLinking()
    {
        using SlangModuleSystem system = new(OptionsFor(SpikeFiles()), null);

        IReadOnlyList<ShaderUniformMember> members = system.GetModuleUniformMembers("test_surface", "materialParams");

        Assert.Multiple(() =>
        {
            Assert.That(members.Select(member => member.Name),
                Is.EqualTo(new[] { "pulseSpeed", "pulseColor", "bandFrequency" }));
            Assert.That(members[0].OffsetBytes, Is.EqualTo(0u));
            Assert.That(members[0].ComponentCount, Is.EqualTo(1));
            Assert.That(members[1].OffsetBytes, Is.EqualTo(16u));
            Assert.That(members[1].ComponentCount, Is.EqualTo(3));
            Assert.That(members[2].OffsetBytes, Is.EqualTo(28u));
        });

        Assert.That(system.GetModuleUniformMembers("test_surface_minimal", "materialParams"),
            Is.Empty, "a module without the block reports empty");
    }

    [Test]
    public void ComposedProgram_RoundTripsThroughDiskCache()
    {
        string cache = Path.Combine(Path.GetTempPath(), $"alco_compose_spike_{Guid.NewGuid():N}");
        try
        {
            byte[][] first;
            using (SlangModuleSystem system = new(OptionsFor(SpikeFiles()), cache))
            {
                using SlangProgram program = system.GetComposedProgram(
                    "test_lit_template", "test_surface", []);
                first = [.. program.EntryCode.Select(code => code.ToArray())];
            }

            using (SlangModuleSystem system = new(OptionsFor(SpikeFiles()), cache))
            {
                using SlangProgram program = system.GetComposedProgram(
                    "test_lit_template", "test_surface", []);
                Assert.Multiple(() =>
                {
                    Assert.That(program.EntryCode[0], Is.EqualTo(first[0]),
                        "the restored program must match the compiled one");
                    Assert.That(program.EntryCode[1], Is.EqualTo(first[1]));
                    Assert.That(program.Reflection.TryGetResourceId("albedoTexture", out _), Is.True);
                });
            }
        }
        finally
        {
            Directory.Delete(cache, true);
        }
    }

    private static Dictionary<string, string> SpikeFiles() => new()
    {
        ["test_contract.slang"] = Contract,
        ["test_lit_template.slang"] = LitTemplate,
        ["test_shadow_template.slang"] = ShadowTemplate,
        ["test_surface.slang"] = Surface,
        ["test_surface_minimal.slang"] = MinimalSurface,
    };

    private static SlangCompilerOptions OptionsFor(Dictionary<string, string> files) => new()
    {
        Resolver = path =>
        {
            string key = SlangPathUtility.NormalizePath(path);
            if (files.TryGetValue(key, out string? content))
                return content;
            string fileName = Path.GetFileName(key);
            return files.FirstOrDefault(pair => Path.GetFileName(pair.Key) == fileName).Value;
        },
        Exists = path => files.ContainsKey(SlangPathUtility.NormalizePath(path)),
    };
}
