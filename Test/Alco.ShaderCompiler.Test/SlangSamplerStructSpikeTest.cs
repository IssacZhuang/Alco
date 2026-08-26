using Alco.Graphics;
using NUnit.Framework;

namespace Alco.ShaderCompiler;

// ─────────────────────────────────────────────────────────────────────────────
// Verified capability: a shared struct type (texture + sampler + sampling
// methods) can replace the per-texture companion declaration, and the type is
// shareable across modules via import. Slang legalizes the nested resource
// fields into the parent block's sequential bindings (depth-first, declaration
// order); reflection exposes them under dotted qualified names. The struct's
// methods ride along, so the shader body reads `_albedo.Sample(uv)` with no
// preprocessor macro.
// ─────────────────────────────────────────────────────────────────────────────
[TestFixture]
public static class SlangSamplerStructSpikeTest
{
    // The shared library module: sampling pairs as types.
    private const string TexLib = """
        #language slang 2025
        module test_texlib;

        public struct Sampled2D
        {
            public Texture2D tex;
            public SamplerState samp;
            public float4 Sample(float2 uv) { return tex.Sample(samp, uv); }
            public float4 SampleLevel(float2 uv, float mip) { return tex.SampleLevel(samp, uv, mip); }
        };

        public struct DepthCmp2D
        {
            public DepthTexture2D tex;
            public SamplerComparisonState samp;
            public float SampleCmp(float2 uv, float compareDepth) { return tex.SampleCmpLevelZero(samp, uv, compareDepth); }
        };
        """;

    // One pass module: two struct instances, a depth-comparison pair and one
    // ordinary member after them.
    private const string PassASource = """
        #language slang 2025
        module test_pass_a;

        import test_texlib;

        cbuffer frame : register(b0, space0)
        {
            float4 time;
        };

        cbuffer pass : register(b0, space1)
        {
            Sampled2D albedo;
            Sampled2D detail;
            DepthCmp2D shadow;
            RWStructuredBuffer<float4> output;
        };

        [shader("fragment")]
        float4 MainPS() : SV_TARGET
        {
            float4 c = albedo.Sample(float2(0.5, 0.5)) + detail.SampleLevel(float2(0.25, 0.25), 2.0);
            c.r += shadow.SampleCmp(float2(0.5, 0.5), 0.5);
            output[0] = c + time;
            return c;
        }
        """;

    // A second pass module importing the same library — the sharing case.
    private const string PassB = """
        #language slang 2025
        module test_pass_b;

        import test_texlib;

        cbuffer frame : register(b0, space0)
        {
            float4 time;
        };

        cbuffer pass : register(b0, space1)
        {
            Sampled2D sceneColor;
        };

        [shader("fragment")]
        float4 MainPS() : SV_TARGET { return sceneColor.Sample(float2(0.5, 0.5)) * time; }
        """;

    // A material-style mixed block: float parameters around a struct pair —
    // the uniform member list must skip the resource-only struct.
    private const string MixedParams = """
        #language slang 2025
        module test_mixed_params;

        import test_texlib;

        cbuffer materialParams
        {
            float pulseSpeed;
            Sampled2D albedo;
            float3 pulseColor;
        };

        [shader("fragment")]
        float4 MainPS() : SV_TARGET
        {
            return albedo.Sample(float2(0.5, 0.5)) * pulseSpeed + float4(pulseColor, 1);
        }
        """;

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

    private static Dictionary<string, string> Files(string extraName, string extraSource) => new()
    {
        ["test_texlib.slang"] = TexLib,
        [extraName] = extraSource,
    };

    private static SlangProgram Compile(string name, string source)
    {
        SlangModuleSystem system = new(OptionsFor(Files($"{name}.slang", source)), null);
        system.AddVirtualModule("test_texlib", TexLib);
        system.AddVirtualModule(name, source);
        system.GetOrLoadModule(name, $"{name}.slang", source);
        return system.GetProgramAllEntries(name, []);
    }

    [Test]
    public static void StructPairMembers_ReflectDottedNames_InSequentialBindings()
    {
        using SlangProgram program = Compile("test_pass_a", PassASource);

        Assert.That(program.Reflection.BindGroups, Has.Count.EqualTo(2));
        Assert.That(program.EntryCode, Has.Length.EqualTo(1));
        Assert.That(program.EntryCode[0][0..4], Is.EqualTo(new byte[] { 0x03, 0x02, 0x23, 0x07 }),
            "the entry must be SPIR-V");

        Assert.That(Entries(program, 1), Is.EqualTo(new[]
        {
            ("albedo.tex", 0u, BindingType.Texture),
            ("albedo.samp", 1u, BindingType.Sampler),
            ("detail.tex", 2u, BindingType.Texture),
            ("detail.samp", 3u, BindingType.Sampler),
            ("shadow.tex", 4u, BindingType.Texture),
            ("shadow.samp", 5u, BindingType.SamplerComparison),
            ("output", 6u, BindingType.StorageBuffer),
        }));
    }

    [Test]
    public static void StructPairCarriesDepthTextureSampleType()
    {
        using SlangProgram program = Compile("test_pass_a", PassASource);

        BindGroupLayout pass = program.Reflection.BindGroups[1];
        BindGroupEntry albedo = pass.Bindings.First(b => b.Entry.Name == "albedo.tex").Entry;
        BindGroupEntry shadow = pass.Bindings.First(b => b.Entry.Name == "shadow.tex").Entry;

        Assert.That(albedo.TextureInfo.SampleType, Is.EqualTo(TextureSampleType.Float));
        Assert.That(shadow.TextureInfo.SampleType, Is.EqualTo(TextureSampleType.Depth));
    }

    [Test]
    public static void StructType_IsSharedAcrossModulesViaImport()
    {
        using SlangProgram program = Compile("test_pass_b", PassB);

        Assert.That(program.Reflection.BindGroups, Has.Count.EqualTo(2));
        Assert.That(Entries(program, 1), Is.EqualTo(new[]
        {
            ("sceneColor.tex", 0u, BindingType.Texture),
            ("sceneColor.samp", 1u, BindingType.Sampler),
        }));
    }

    [Test]
    public static void MixedParamsBlock_UniformMembersSkipResourceOnlyStruct()
    {
        using SlangProgram program = Compile("test_mixed_params", MixedParams);

        IReadOnlyList<ShaderUniformMember> members = program.GetUniformMembers("materialParams");
        Assert.That(members.Select(m => m.Name), Is.EqualTo(new[] { "pulseSpeed", "pulseColor" }));
        Assert.That(members[0].OffsetBytes, Is.EqualTo(0u));
    }

    private static (string Name, uint Binding, BindingType Type)[] Entries(SlangProgram program, int group)
    {
        return program.Reflection.BindGroups[group].Bindings
            .Select(info => (info.Entry.Name, info.Entry.Binding, info.Entry.Type))
            .ToArray();
    }
}
