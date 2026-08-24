using NUnit.Framework;

namespace Alco.ShaderCompiler;

// ─────────────────────────────────────────────────────────────────────────────
// Spike for the marked material-parameter blocks: a surface marks its parameter
// cbuffers with a user-defined attribute ([MaterialParams]) instead of following
// a fixed block-name convention. Proves the slang mechanisms the discovery is
// built on:
//
//   1. a user-defined attribute declared in one module is usable from an
//      importing module and is visible on the cbuffer VARIABLE through slang's
//      reflection (spReflectionVariable_GetUserAttribute*), at module level —
//      no entry points, no link;
//   2. discovery finds marked blocks under ANY name, several per module, and
//      skips unmarked engine blocks (the _globalRenderData case) and
//      resource-only blocks;
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

        cbuffer _globalRenderData : register(b0, space2)
        {
            float4 time;
        }

        [MaterialParams]
        cbuffer PulseParams : register(b1, space2)
        {
            float pulseSpeed;
            float3 pulseColor;
        }

        [MaterialParams]
        cbuffer WindParams : register(b2, space2)
        {
            float windStrength;

            Texture2D<float4> _noiseTexture;
            SamplerState _noiseTextureSampler;
        }
        """;

    private const string Plain = """
        #language slang 2025
        module test_marked_plain;

        import test_marked_contract;

        cbuffer _unmarked : register(b0, space2)
        {
            float anything;
        }
        """;

    [Test]
    public void ModuleLayout_DiscoversMarkedBlocks_ByAttributeNotName()
    {
        using SlangModuleSystem system = new(OptionsFor(Files()), null);

        var blocks = system.GetModuleMarkedUniformBlocks("test_marked_surface", "MaterialParams");

        Assert.Multiple(() =>
        {
            Assert.That(blocks.Select(block => block.BlockName),
                Is.EqualTo(new[] { "PulseParams", "WindParams" }),
                "discovery is attribute-driven: free names, declaration order, no _globalRenderData");
            Assert.That(blocks[0].Members.Select(member => member.Name),
                Is.EqualTo(new[] { "pulseSpeed", "pulseColor" }));
            Assert.That(blocks[0].Members[1].OffsetBytes, Is.EqualTo(16u));
            Assert.That(blocks[1].Members.Select(member => member.Name),
                Is.EqualTo(new[] { "windStrength" }),
                "the texture/sampler members of a mixed block are binding entries, not uniform members");
        });
    }

    [Test]
    public void ModuleLayout_ModuleWithoutMarkedBlocks_ReportsEmpty()
    {
        using SlangModuleSystem system = new(OptionsFor(Files()), null);

        Assert.That(system.GetModuleMarkedUniformBlocks("test_marked_plain", "MaterialParams"), Is.Empty);
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
