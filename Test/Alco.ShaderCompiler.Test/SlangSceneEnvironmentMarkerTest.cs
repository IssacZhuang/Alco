using Alco.Graphics;
using NUnit.Framework;

namespace Alco.ShaderCompiler;

// Spike for the [SceneEnvironmentParams] marker the PBR scene environment
// discovers its lighting-data block by (PBRSceneEnvironment.EnvironmentDataMarker):
//
//   1. a module can declare the user-defined attribute struct and use it on the
//      same file's ParameterBlock<T> global (the World3D PBR common module owns
//      its marker — no separate contract import), unlike the material system's
//      cross-module [MaterialParams];
//   2. module reflection exposes the marked ParameterBlock variable as a uniform
//      block carrying the attribute, its members listed by bare field name at
//      their reflected offsets — the exact vocabulary UniformGraphicsBuffer
//      writes through.
[TestFixture]
public class SlangSceneEnvironmentMarkerTest
{
    // The PBR common module's shape, in miniature: attribute declared and
    // applied in one file, block variable of ParameterBlock<Struct> type,
    // mixed float/uint/bool members plus a fixed-size matrix array.
    private const string Module = """
        #language slang 2025
        module test_env_marker;

        [__AttributeUsage(_AttributeTargets.Var)]
        public struct SceneEnvironmentParams {};

        public struct EnvData
        {
            public float4x4 invViewProjection;
            public float4x4 sunViewProjection[4];
            public float4 sunDirection;
            public bool   shadowEnabled;
            public uint   numPointLights;
        };

        [SceneEnvironmentParams]
        public ParameterBlock<EnvData> data;

        // An unmarked sibling block the discovery must skip.
        cbuffer engineData : register(b0, space2)
        {
            float4 time;
        }
        """;

    [Test]
    public void ModuleReflection_MarkerOnParameterBlockVariable_IsVisible()
    {
        using SlangModuleSystem system = new(OptionsFor(Files()), null);

        ShaderLibraryReflection reflection = system.GetModuleReflection("test_env_marker");

        ShaderUniformBlock[] marked = reflection.UniformBlocks
            .Where(block => block.Attributes.Contains("SceneEnvironmentParams"))
            .ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(marked.Select(block => block.Name), Is.EqualTo(new[] { "data" }),
                "exactly the marked ParameterBlock variable, not the unmarked engine cbuffer");
            Assert.That(marked[0].Attributes, Is.EqualTo(new[] { "SceneEnvironmentParams" }));
        });
    }

    [Test]
    public void ModuleReflection_MarkedBlockMembers()
    {
        using SlangModuleSystem system = new(OptionsFor(Files()), null);

        ShaderLibraryReflection reflection = system.GetModuleReflection("test_env_marker");
        ShaderUniformBlock data = reflection.UniformBlocks.Single(block => block.Name == "data");

        Assert.Multiple(() =>
        {
            Assert.That(data.UnsupportedMemberReason, Is.Null);
            Assert.That(data.Members.Select(member => member.Name), Is.EqualTo(new[]
                { "invViewProjection", "sunViewProjection", "sunDirection", "shadowEnabled", "numPointLights" }));
            // The matrix array keeps its element count — the cascade VP write path.
            Assert.That(data.Members[1].ElementCount, Is.EqualTo(4));
            // bool/uint marshal through their own scalar kinds, same as PbrData.
            Assert.That(data.Members[3].ScalarType, Is.EqualTo(ShaderUniformScalarType.Bool32));
            Assert.That(data.Members[4].ScalarType, Is.EqualTo(ShaderUniformScalarType.UInt32));
        });
    }

    private static Dictionary<string, string> Files() => new()
    {
        ["test_env_marker.slang"] = Module,
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
