using Alco.Graphics;
using NUnit.Framework;

namespace Alco.ShaderCompiler;

// ─────────────────────────────────────────────────────────────────────────────
// Spike for the marked material-parameter blocks: a surface marks its parameter
// cbuffers with the user-defined attribute ([MaterialParams]) — block names stay
// free and discovery keys off an explicit source marker. The compiler layer
// reflects every block with its user attributes (domain-neutral
// GetModuleReflection); marker filtering is the material domain's job. Proves
// the slang mechanisms the discovery is built on:
//
//   1. a user-defined attribute declared in one module is usable from an
//      importing module and is visible on the cbuffer VARIABLE through slang's
//      reflection (spReflectionVariable_GetUserAttribute*), at module level —
//      no entry points, no link;
//   2. reflection lists blocks under ANY name with their attributes, and the
//      material-side filter keeps the marked ones, skipping unmarked engine
//      blocks (the _globalRenderData case) and resource-only blocks;
//   3. a marked block keeps mixing uniform members with texture/sampler
//      resources (the mixed-block shape the material set already uses).
// ─────────────────────────────────────────────────────────────────────────────
[TestFixture]
public class SlangMarkedUniformBlocksTest
{
    // The contract module declares the marker attribute once; every surface
    // imports it with the contract.
    private const string Contract = """
        #language slang 2025
        module test_marked_contract;

        [__AttributeUsage(_AttributeTargets.Var)]
        public struct MaterialParams {};
        """;

    // A surface with two freely-named marked blocks (one pure, one mixed with
    // the surface's textures) plus an unmarked engine data block.
    private const string Surface = """
        #language slang 2025
        module test_marked_surface;

        import test_marked_contract;

        cbuffer globalRenderData : register(b0, space2)
        {
            float4 time;
        }

        [MaterialParams]
        cbuffer pulseParams : register(b1, space2)
        {
            float pulseSpeed;
            float3 pulseColor;
        }

        [MaterialParams]
        cbuffer windParams : register(b2, space2)
        {
            float windStrength;

            Texture2D<float4> noiseTexture;
            SamplerState noiseTextureSampler;
        }
        """;

    private const string Plain = """
        #language slang 2025
        module test_marked_plain;

        import test_marked_contract;

        cbuffer unmarked : register(b0, space2)
        {
            float anything;
        }
        """;

    [Test]
    public void ModuleReflection_ListsEveryBlockWithAttributes_FilterKeepsMarkedOnes()
    {
        using SlangModuleSystem system = new(OptionsFor(Files()), null);

        ShaderLibraryReflection reflection = system.GetModuleReflection("test_marked_surface");

        Assert.Multiple(() =>
        {
            Assert.That(reflection.UniformBlocks.Select(block => block.Name),
                Is.EqualTo(new[] { "globalRenderData", "pulseParams", "windParams" }),
                "module reflection is exhaustive and domain-neutral: every block, declaration order");
            var marked = reflection.UniformBlocks
                .Where(block => block.Attributes.Contains("MaterialParams"))
                .Select(block => block.Name)
                .ToArray();
            Assert.That(marked, Is.EqualTo(new[] { "pulseParams", "windParams" }),
                "the material-domain filter is attribute-driven: free names, no globalRenderData");
        });
    }

    [Test]
    public void ModuleReflection_MarkedBlockMembersAndTextureSlots()
    {
        using SlangModuleSystem system = new(OptionsFor(Files()), null);

        ShaderLibraryReflection reflection = system.GetModuleReflection("test_marked_surface");
        ShaderUniformBlock pulse = reflection.UniformBlocks.First(block => block.Name == "pulseParams");
        ShaderUniformBlock wind = reflection.UniformBlocks.First(block => block.Name == "windParams");

        Assert.Multiple(() =>
        {
            Assert.That(pulse.UnsupportedMemberReason, Is.Null);
            Assert.That(pulse.Members.Select(member => member.Name),
                Is.EqualTo(new[] { "pulseSpeed", "pulseColor" }));
            Assert.That(pulse.Members[1].OffsetBytes, Is.EqualTo(16u));
            Assert.That(wind.Members.Select(member => member.Name),
                Is.EqualTo(new[] { "windStrength" }),
                "the texture/sampler members of a mixed block are binding entries, not uniform members");
            Assert.That(reflection.TextureSlots.Select(slot => slot.Name), Is.EqualTo(new[] { "noiseTexture" }));
            Assert.That(reflection.TextureSlots[0].ViewDimension, Is.EqualTo(TextureViewDimension.Texture2D));
            Assert.That(reflection.TextureSlots[0].SampleType, Is.EqualTo(TextureSampleType.Float));
            Assert.That(reflection.SamplerSlots.Select(slot => slot.Name),
                Is.EqualTo(new[] { "noiseTextureSampler" }));
            Assert.That(reflection.SamplerSlots[0].IsComparison, Is.False);
        });
    }

    [Test]
    public void ModuleReflection_PlainModule_HasNoMarkedBlocks()
    {
        using SlangModuleSystem system = new(OptionsFor(Files()), null);

        ShaderLibraryReflection reflection = system.GetModuleReflection("test_marked_plain");

        Assert.That(reflection.UniformBlocks
            .Where(block => block.Attributes.Contains("MaterialParams")), Is.Empty);
    }

    private static Dictionary<string, string> Files() => new()
    {
        ["test_marked_contract.slang"] = Contract,
        ["test_marked_surface.slang"] = Surface,
        ["test_marked_plain.slang"] = Plain,
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
