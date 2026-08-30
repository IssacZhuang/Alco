using Alco.Graphics;
using NUnit.Framework;

namespace Alco.ShaderCompiler;

/// <summary>
/// Temporary verification: portable system-value semantics (SV_InstanceID /
/// SV_VertexID) must compile for both Spirv and Dxil targets, while the
/// Vulkan-only aliases (SV_VulkanInstanceID / SV_VulkanVertexID) must fail
/// on Dxil (dxc rejects them — sandbox 34 bug) and succeed on Spirv.
/// </summary>
[TestFixture]
public class SlangPortableSemanticProbe
{
    private const string InstancedShader = """
        struct VSInput
        {
            float3 position : POSITION;
            uint   vertexId : VERTEX_ID_SEMANTIC;
            uint instanceId : INSTANCE_ID_SEMANTIC;
        };

        struct VSOutput
        {
            float4 position : SV_POSITION;
            float2 data     : TEXCOORD0;
        };

        [shader("vertex")]
        VSOutput MainVS(VSInput input)
        {
            VSOutput output;
            float x = float(input.instanceId) * 0.1;
            float y = float(input.vertexId) * 0.1;
            output.position = float4(input.position, 1.0);
            output.data = float2(x, y);
            return output;
        }
        """;

    private static string WithSemantics(string instanceSemantic, string vertexSemantic)
        => InstancedShader
            .Replace("INSTANCE_ID_SEMANTIC", instanceSemantic)
            .Replace("VERTEX_ID_SEMANTIC", vertexSemantic);

    private static SlangModuleSystem CreateSystem(Dictionary<string, string> files, SlangCodeTarget target)
    {
        SlangCompilerOptions options = new()
        {
            Target = target,
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
        return new SlangModuleSystem(options, null);
    }

    private static (byte[] Code, string Summary) CompileVertex(Dictionary<string, string> files, SlangCodeTarget target)
    {
        using SlangModuleSystem system = CreateSystem(files, target);
        system.GetOrLoadModule("probe", "probe.slang", files["probe.slang"]);
        using SlangProgram program = system.GetProgram("probe",
            [new SlangEntryPointRequest("MainVS", ShaderStage.Vertex)], []);
        byte[] code = program.EntryCode[0];
        return (code, $"{target}: {code.Length} bytes");
    }

    private static HashSet<uint> SpirvBuiltins(byte[] spirv)
    {
        Assert.That(spirv.Length > 20, "not a SPIR-V blob");
        Assert.That(BitConverter.ToUInt32(spirv, 0), Is.EqualTo(0x07230203u), "SPIR-V magic");
        HashSet<uint> builtins = [];
        int offset = 20; // header is 5 words
        while (offset + 4 <= spirv.Length)
        {
            uint word0 = BitConverter.ToUInt32(spirv, offset);
            uint wordCount = word0 >> 16;
            uint opcode = word0 & 0xFFFF;
            if (wordCount == 0)
                break;
            if (opcode == 71 && offset + 12 <= spirv.Length) // OpDecorate
            {
                uint decoration = BitConverter.ToUInt32(spirv, offset + 8);
                if (decoration == 11) // BuiltIn
                    builtins.Add(BitConverter.ToUInt32(spirv, offset + 12));
            }
            offset += (int)wordCount * 4;
        }
        return builtins;
    }

    [Test]
    public void PortableSemantics_CompileForDxil()
    {
        Dictionary<string, string> files = new() { ["probe.slang"] = WithSemantics("SV_InstanceID", "SV_VertexID") };
        Assert.DoesNotThrow(() => CompileVertex(files, SlangCodeTarget.Dxil));
    }

    [Test]
    public void PortableSemantics_CompileForSpirv_AndMapToVertexIndexBuiltins()
    {
        Dictionary<string, string> files = new() { ["probe.slang"] = WithSemantics("SV_InstanceID", "SV_VertexID") };
        (byte[] code, string summary) = CompileVertex(files, SlangCodeTarget.Spirv);
        HashSet<uint> builtins = SpirvBuiltins(code);
        TestContext.Out.WriteLine($"builtins: {string.Join(",", builtins.OrderBy(v => v))} ({summary})");
        // 42 = VertexIndex, 43 = InstanceIndex (DXC-compatible mapping)
        Assert.That(builtins, Has.Member(43u), "SV_InstanceID must map to BuiltIn InstanceIndex");
        Assert.That(builtins, Has.Member(42u), "SV_VertexID must map to BuiltIn VertexIndex");
    }

    [Test]
    public void VulkanOnlySemantics_FailForDxil_SucceedForSpirv()
    {
        Dictionary<string, string> files = new() { ["probe.slang"] = WithSemantics("SV_VulkanInstanceID", "SV_VulkanVertexID") };
        Assert.Throws<InvalidOperationException>(() => CompileVertex(files, SlangCodeTarget.Dxil),
            "Vulkan-only semantics must fail on the Dxil target (the sandbox 34 bug)");
        Assert.DoesNotThrow(() => CompileVertex(files, SlangCodeTarget.Spirv));
    }

    [Test]
    public void SemanticMapping_ComparePerSemantic()
    {
        (byte[] portableInstance, _) = CompileVertex(
            new Dictionary<string, string> { ["probe.slang"] = WithSemantics("SV_InstanceID", "SV_VertexID") },
            SlangCodeTarget.Spirv);
        (byte[] vulkanInstance, _) = CompileVertex(
            new Dictionary<string, string> { ["probe.slang"] = WithSemantics("SV_VulkanInstanceID", "SV_VulkanVertexID") },
            SlangCodeTarget.Spirv);
        TestContext.Out.WriteLine($"portable  (SV_InstanceID / SV_VertexID):        {Describe(SpirvBuiltins(portableInstance))}");
        TestContext.Out.WriteLine($"vulkan-only (SV_VulkanInstanceID / SV_VulkanVertexID): {Describe(SpirvBuiltins(vulkanInstance))}");
        // BuiltIn values: 42 VertexIndex, 43 InstanceIndex, 4424 BaseVertex, 4425 BaseInstance.
        // Presence of Base* decorations means the shader computes id - base
        // (the draw-relative GLSL gl_VertexID / gl_InstanceID semantics).
        Assert.That(SpirvBuiltins(vulkanInstance), Does.Contain(4425u),
            "SV_VulkanInstanceID must resolve through gl_InstanceID (InstanceIndex - BaseInstance)");
        Assert.That(SpirvBuiltins(portableInstance), Does.Not.Contain(4425u),
            "SV_InstanceID must map directly to BuiltIn InstanceIndex");
    }

    private static string Describe(HashSet<uint> builtins)
        => string.Join(", ", builtins.OrderBy(v => v).Select(v => v switch
        {
            42u => "42(VertexIndex)",
            43u => "43(InstanceIndex)",
            4424u => "4424(BaseVertex)",
            4425u => "4425(BaseInstance)",
            _ => v.ToString(),
        }));
}
