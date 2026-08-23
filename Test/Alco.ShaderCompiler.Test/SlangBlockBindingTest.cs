using Alco.Graphics;
using NUnit.Framework;

namespace Alco.ShaderCompiler;

// The set-only binding contract: a program declares each set as one block
// (`cbuffer _name : register(b0, spaceN) { ... }` / ConstantBuffer<T> with a
// register space). The compiler assigns member bindings in declaration order;
// reflection exposes members by their bare field name and the block's uniform
// data as one buffer entry. C# never reads a binding number.

[TestFixture]
public static class SlangBlockBindingTest
{
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

    private static SlangProgram Compile(string name, string source)
    {
        Dictionary<string, string> files = new() { [$"{name}.slang"] = source };
        SlangModuleSystem system = new(OptionsFor(files), null);
        system.AddVirtualModule(name, source);
        system.GetOrLoadModule(name, $"{name}.slang", source);
        return system.GetProgramAllEntries(name, []);
    }

    private const string MixedBlocks = """
        module block_mixed;

        struct FrameParams
        {
            float4x4 viewProjection;
            float time;
            Texture2D blueNoise;
            SamplerState noiseSampler;
        };

        ConstantBuffer<FrameParams> _frame : register(b0, space0);

        cbuffer _pass : register(b0, space1)
        {
            Texture2D sceneColor;
            SamplerState sceneSampler;
            RWStructuredBuffer<float4> output;
            [[vk::image_format("rgba16f")]] RWTexture2D<float4> indirectGI;
            DepthTexture2D sceneDepth;
            SamplerComparisonState sceneDepthSampler;
        };

        [shader("fragment")]
        float4 MainPS() : SV_TARGET
        {
            output[0] = sceneColor.Sample(sceneSampler, float2(0.5, 0.5));
            indirectGI[int2(0, 0)] = float4(1);
            float d = sceneDepth.SampleCmpLevelZero(sceneDepthSampler, float2(0.5, 0.5), 0.5);
            return _frame.blueNoise.Sample(_frame.noiseSampler, float2(_frame.time, 0)) + _frame.viewProjection[0] + d;
        }
        """;

    [Test]
    public static void BlockMembers_ReflectByBareName_InCompilerAssignedOrder()
    {
        using SlangProgram program = Compile("block_mixed", MixedBlocks);

        Assert.That(program.Reflection.BindGroups, Has.Count.EqualTo(2));
        Assert.That(program.Reflection.BindGroups[0].Group, Is.EqualTo(0u));
        Assert.That(program.Reflection.BindGroups[1].Group, Is.EqualTo(1u));

        // Set 0: the block's uniform buffer first, then resource members in
        // declaration order.
        Assert.That(Entries(program, 0), Is.EqualTo(new[]
        {
            ("_frame", 0u, BindingType.UniformBuffer),
            ("blueNoise", 1u, BindingType.Texture),
            ("noiseSampler", 2u, BindingType.Sampler),
        }));

        // Set 1: a resource-only block emits no uniform buffer entry.
        Assert.That(Entries(program, 1), Is.EqualTo(new[]
        {
            ("sceneColor", 0u, BindingType.Texture),
            ("sceneSampler", 1u, BindingType.Sampler),
            ("output", 2u, BindingType.StorageBuffer),
            ("indirectGI", 3u, BindingType.StorageTexture),
            ("sceneDepth", 4u, BindingType.Texture),
            ("sceneDepthSampler", 5u, BindingType.SamplerComparison),
        }));
    }

    [Test]
    public static void UniformMembers_SkipResourceFields()
    {
        using SlangProgram program = Compile("block_mixed", MixedBlocks);
        List<SlangUniformMember> members = program.GetUniformMembers("_frame");

        Assert.That(members.Select(m => m.Name), Is.EqualTo(new[] { "viewProjection", "time" }));
        Assert.That(members[0].OffsetBytes, Is.EqualTo(0u));
        Assert.That(members[1].OffsetBytes, Is.GreaterThan(0u));
    }

    [Test]
    public static void ResourceNames_MustBeUniqueAcrossSets()
    {
        // Slang itself rejects the same-scope duplicate as an ambiguous
        // reference; the reflection bridge rejects multi-module shadowing.
        // Either way the duplicate fails at compile time, never silently.
        const string duplicate = """
            module block_dup;

            cbuffer _first : register(b0, space0)
            {
                Texture2D shared;
            };

            cbuffer _second : register(b0, space1)
            {
                Texture2D shared;
            };

            [shader("fragment")]
            float4 MainPS() : SV_TARGET { return shared; }
            """;

        Assert.That(() => Compile("block_dup", duplicate), Throws.Exception);
    }

    [Test]
    public static void FlatDeclarations_AutoAssignIntoSetZero()
    {
        const string flat = """
            module block_flat;

            cbuffer _data { float4x4 viewProjection; float time; };
            Texture2D _tex;
            SamplerState _texSampler;
            RWStructuredBuffer<float4> _output;

            [shader("fragment")]
            float4 MainPS() : SV_TARGET
            {
                _output[0] = _tex.Sample(_texSampler, float2(_time_placeholder(), 0.5));
                return viewProjection[0];
            }

            float _time_placeholder() { return time; }
            """;

        using SlangProgram program = Compile("block_flat", flat);

        Assert.That(program.Reflection.BindGroups, Has.Count.EqualTo(1));
        Assert.That(program.Reflection.BindGroups[0].Group, Is.EqualTo(0u));
        Assert.That(Entries(program, 0), Is.EqualTo(new[]
        {
            ("_data", 0u, BindingType.UniformBuffer),
            ("_tex", 1u, BindingType.Texture),
            ("_texSampler", 2u, BindingType.Sampler),
            ("_output", 3u, BindingType.StorageBuffer),
        }));
    }

    [Test]
    public static void MultipleBlocks_ShareOneSetWithMixedBlockLast()
    {
        // The material surface contract: one set owned by the module. Pure UBO
        // blocks come first (sequential b-registers); the mixed block that
        // carries both uniform parameters and resources comes last so its
        // members continue past the UBOs. A register on a resource-only block
        // is ignored by slang — its members would restart at 0 and collide —
        // so resource-only blocks must always own their set alone.
        const string multi = """
            module block_multi;

            cbuffer _globalRenderData : register(b0, space0)
            {
                float4 time;
            };

            cbuffer _materialParams : register(b1, space0)
            {
                float pulseSpeed;
                float3 pulseColor;
                Texture2D albedo;
                SamplerState albedoSampler;
                RWStructuredBuffer<float4> instances;
            };

            [shader("fragment")]
            float4 MainPS() : SV_TARGET
            {
                instances[0] = float4(pulseColor * pulseSpeed, time.x);
                return albedo.Sample(albedoSampler, float2(0.5, 0.5)) + time;
            }
            """;

        using SlangProgram program = Compile("block_multi", multi);

        Assert.That(program.Reflection.BindGroups, Has.Count.EqualTo(1));
        Assert.That(program.Reflection.BindGroups[0].Group, Is.EqualTo(0u));
        Assert.That(Entries(program, 0), Is.EqualTo(new[]
        {
            ("_globalRenderData", 0u, BindingType.UniformBuffer),
            ("_materialParams", 1u, BindingType.UniformBuffer),
            ("albedo", 2u, BindingType.Texture),
            ("albedoSampler", 3u, BindingType.Sampler),
            ("instances", 4u, BindingType.StorageBuffer),
        }));
        List<SlangUniformMember> members = program.GetUniformMembers("_materialParams");
        Assert.That(members.Select(m => m.Name), Is.EqualTo(new[] { "pulseSpeed", "pulseColor" }));
    }

    private static (string Name, uint Binding, BindingType Type)[] Entries(SlangProgram program, int group)
    {
        return program.Reflection.BindGroups[group].Bindings
            .Select(info => (info.Entry.Name, info.Entry.Binding, info.Entry.Type))
            .ToArray();
    }
}
