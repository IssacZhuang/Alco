using Alco.Graphics;
using NUnit.Framework;

namespace Alco.ShaderCompiler;

// The ParameterBlock binding contract: a program declares each resource group
// as one annotation-free `ParameterBlock<BlockParams> _name;`. The compiler
// owns the layout — each block takes one whole descriptor set, in declaration
// order, with an automatically-introduced uniform buffer at binding 0 whenever
// the block carries ordinary data (resource members continue after it).
// Reflection exposes members by their bare field name and the block's uniform
// data as one buffer entry under the block's name. C# never reads a set number.

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

        public struct FrameParams
        {
            public float4x4 viewProjection;
            public float time;
            public Texture2D blueNoise;
            public SamplerState noiseSampler;
        };

        public ParameterBlock<FrameParams> _frame;

        public struct PassParams
        {
            public Texture2D sceneColor;
            public SamplerState sceneSampler;
            public RWStructuredBuffer<float4> output;
            [[vk::image_format("rgba16f")]] public RWTexture2D<float4> indirectGI;
            public DepthTexture2D sceneDepth;
            public SamplerComparisonState sceneDepthSampler;
        };

        public ParameterBlock<PassParams> _pass;

        [shader("fragment")]
        float4 MainPS() : SV_TARGET
        {
            _pass.output[0] = _pass.sceneColor.Sample(_pass.sceneSampler, float2(0.5, 0.5));
            _pass.indirectGI[int2(0, 0)] = float4(1);
            float d = _pass.sceneDepth.SampleCmpLevelZero(_pass.sceneDepthSampler, float2(0.5, 0.5), 0.5);
            return _frame.blueNoise.Sample(_frame.noiseSampler, float2(_frame.time, 0)) + _frame.viewProjection[0] + d;
        }
        """;

    [Test]
    public static void BlockMembers_ReflectByBareName_EachBlockOwnsOneSet()
    {
        using SlangProgram program = Compile("block_mixed", MixedBlocks);

        Assert.That(program.Reflection.BindGroups, Has.Count.EqualTo(2));
        Assert.That(program.Reflection.BindGroups[0].Group, Is.EqualTo(0u));
        Assert.That(program.Reflection.BindGroups[1].Group, Is.EqualTo(1u));

        // Set 0: the block's automatically-introduced uniform buffer at
        // binding 0, then resource members in declaration order.
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
        IReadOnlyList<ShaderUniformMember> members = program.GetUniformMembers("_frame");

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

            public struct FirstParams
            {
                public Texture2D shared;
            };

            public ParameterBlock<FirstParams> _first;

            public struct SecondParams
            {
                public Texture2D shared;
            };

            public ParameterBlock<SecondParams> _second;

            [shader("fragment")]
            float4 MainPS() : SV_TARGET { return shared; }
            """;

        Assert.That(() => Compile("block_dup", duplicate), Throws.Exception);
    }

    [Test]
    public static void BareGlobals_TakeSetZero_BlocksGetTheirOwnSetsAfter()
    {
        // Under auto layout bare globals (no block) fill set 0 first; every
        // ParameterBlock then owns its own whole set, in declaration order.
        // No mixed sets: a block never shares with loose globals.
        const string flat = """
            module block_flat;

            public struct DataParams { public float4x4 viewProjection; public float time; };
            public ParameterBlock<DataParams> _data;
            Texture2D _tex;
            SamplerState _texSampler;
            RWStructuredBuffer<float4> _output;

            [shader("fragment")]
            float4 MainPS() : SV_TARGET
            {
                _output[0] = _tex.Sample(_texSampler, float2(_time_placeholder(), 0.5));
                return _data.viewProjection[0];
            }

            float _time_placeholder() { return _data.time; }
            """;

        using SlangProgram program = Compile("block_flat", flat);

        Assert.That(program.Reflection.BindGroups, Has.Count.EqualTo(2));
        Assert.That(program.Reflection.BindGroups[0].Group, Is.EqualTo(0u));
        Assert.That(Entries(program, 0), Is.EqualTo(new[]
        {
            ("_tex", 0u, BindingType.Texture),
            ("_texSampler", 1u, BindingType.Sampler),
            ("_output", 2u, BindingType.StorageBuffer),
        }));
        Assert.That(Entries(program, 1), Is.EqualTo(new[]
        {
            ("_data", 0u, BindingType.UniformBuffer),
        }));
    }

    [Test]
    public static void EachBlock_TakesItsOwnSet_InDeclarationOrder()
    {
        // The material surface shape: the engine-data block and the marked
        // parameter block are separate ParameterBlocks, so each owns its own
        // set — no more shared-set b-register arithmetic (the UBO of a mixed
        // block always restarts at binding 0 of its set).
        const string multi = """
            module block_multi;

            public struct GlobalRenderDataParams
            {
                public float4 time;
            };

            public ParameterBlock<GlobalRenderDataParams> _globalRenderData;

            [MaterialParams]
            public struct MaterialParamsData
            {
                public float pulseSpeed;
                public float3 pulseColor;
                public Texture2D albedo;
                public SamplerState albedoSampler;
                public RWStructuredBuffer<float4> instances;
            };

            public ParameterBlock<MaterialParamsData> _materialParams;

            [shader("fragment")]
            float4 MainPS() : SV_TARGET
            {
                _materialParams.instances[0] = float4(_materialParams.pulseColor * _materialParams.pulseSpeed, _globalRenderData.time.x);
                return _materialParams.albedo.Sample(_materialParams.albedoSampler, float2(0.5, 0.5)) + _globalRenderData.time;
            }
            """;

        using SlangProgram program = Compile("block_multi", multi);

        Assert.That(program.Reflection.BindGroups, Has.Count.EqualTo(2));
        Assert.That(program.Reflection.BindGroups[0].Group, Is.EqualTo(0u));
        Assert.That(Entries(program, 0), Is.EqualTo(new[]
        {
            ("_globalRenderData", 0u, BindingType.UniformBuffer),
        }));
        Assert.That(Entries(program, 1), Is.EqualTo(new[]
        {
            ("_materialParams", 0u, BindingType.UniformBuffer),
            ("albedo", 1u, BindingType.Texture),
            ("albedoSampler", 2u, BindingType.Sampler),
            ("instances", 3u, BindingType.StorageBuffer),
        }));
        IReadOnlyList<ShaderUniformMember> members = program.GetUniformMembers("_materialParams");
        Assert.That(members.Select(m => m.Name), Is.EqualTo(new[] { "pulseSpeed", "pulseColor" }));
    }

    private static (string Name, uint Binding, BindingType Type)[] Entries(SlangProgram program, int group)
    {
        return program.Reflection.BindGroups[group].Bindings
            .Select(info => (info.Entry.Name, info.Entry.Binding, info.Entry.Type))
            .ToArray();
    }
}
