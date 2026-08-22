using Alco.Graphics;
using NUnit.Framework;

namespace Alco.ShaderCompiler;

/// <summary>
/// Smoke tests for the modern slang API binding (Binding/Slang/). These verify
/// the vtable layouts against the pinned slang release — any layout mismatch
/// is a native crash or garbage reflection, so the assertions double as
/// canaries for slang upgrades.
/// </summary>
[TestFixture]
public class SlangNativeApiTest
{
    private const string GraphicsShader = """
        cbuffer _frame : register(b0, space0)
        {
            float4x4 viewProjection;
        };

        Texture2D _albedo        : register(t0, space1);
        SamplerState _albedoSampler : register(s0, space1);

        struct VSInput
        {
            float3 position : POSITION;
            float2 uv       : TEXCOORD0;
        };

        struct VSOutput
        {
            float4 position : SV_POSITION;
            float2 uv       : TEXCOORD0;
        };

        [shader("vertex")]
        VSOutput MainVS(VSInput input)
        {
            VSOutput output;
            output.position = mul(viewProjection, float4(input.position, 1.0));
            output.uv = input.uv;
            return output;
        }

        [shader("fragment")]
        float4 MainPS(VSOutput input) : SV_TARGET
        {
            return _albedo.Sample(_albedoSampler, input.uv);
        }
        """;

    private const string ComputeShader = """
        RWStructuredBuffer<float> _buffer : register(u0, space0);

        [numthreads(8, 4, 1)]
        [shader("compute")]
        void MainCS(uint3 dispatchThreadID : SV_DispatchThreadID)
        {
            _buffer[dispatchThreadID.x] = float(dispatchThreadID.y);
        }
        """;

    private const string ImportableLib = """
        export float4 LibColor()
        {
            return float4(0, 1, 0, 1);
        }
        """;

    private const string ImportingShader = """
        import alco_test_lib;

        [shader("fragment")]
        float4 MainPS() : SV_TARGET
        {
            return LibColor();
        }
        """;

    [Test]
    public void CreateGlobalSession_ReturnsBuildTag()
    {
        using SlangCompiler compiler = SlangCompiler.Create();
        Assert.That(compiler.BuildTag, Is.Not.Empty);
        Assert.That(compiler.BuildTag, Does.Contain("2026"));
    }

    [Test]
    public void Compile_GraphicsShader_ReturnsSpirvAndReflection()
    {
        using SlangCompiler compiler = SlangCompiler.Create();
        using SlangCompileSession session = compiler.CreateSession(SlangCompilerOptions.Default);
        SlangModuleHandle module = session.LoadModuleFromSource("alco_test_graphics", "alco_test_graphics.slang", GraphicsShader);
        using SlangProgram program = session.Compile(module,
        [
            new SlangEntryPointRequest("MainVS", ShaderStage.Vertex),
            new SlangEntryPointRequest("MainPS", ShaderStage.Fragment),
        ]);

        Assert.Multiple(() =>
        {
            // SPIR-V magic number: 0x07230203 (little-endian bytes: 03 02 23 07)
            Assert.That(program.EntryCode.Length, Is.EqualTo(2));
            Assert.That(program.EntryCode[0][0..4], Is.EqualTo(new byte[] { 0x03, 0x02, 0x23, 0x07 }));
            Assert.That(program.EntryCode[1][0..4], Is.EqualTo(new byte[] { 0x03, 0x02, 0x23, 0x07 }));
        });
    }

    [Test]
    public void Compile_GraphicsShader_ReflectionMatchesDeclarations()
    {
        using SlangCompiler compiler = SlangCompiler.Create();
        using SlangCompileSession session = compiler.CreateSession(SlangCompilerOptions.Default);
        SlangModuleHandle module = session.LoadModuleFromSource("alco_test_graphics2", "alco_test_graphics2.slang", GraphicsShader);
        using SlangProgram program = session.Compile(module,
        [
            new SlangEntryPointRequest("MainVS", ShaderStage.Vertex),
            new SlangEntryPointRequest("MainPS", ShaderStage.Fragment),
        ]);

        ShaderReflectionInfo reflection = program.Reflection;
        Assert.Multiple(() =>
        {
            // space0: _frame (uniform); space1: _albedo (texture) + _albedoSampler (sampler)
            Assert.That(reflection.BindGroups.Count, Is.EqualTo(2), reflection.ToString());

            BindGroupLayout frameGroup = reflection.BindGroups[0];
            Assert.That(frameGroup.Group, Is.EqualTo(0u));
            Assert.That(frameGroup.Bindings.Count, Is.EqualTo(1));
            Assert.That(frameGroup.Bindings[0].Entry.Name, Is.EqualTo("_frame"));
            Assert.That(frameGroup.Bindings[0].Entry.Type, Is.EqualTo(BindingType.UniformBuffer));
            // Bindings carry the engine's Standard (V|F|C) visibility, matching
            // the DXC SPIR-V reflector (ResolveEffectiveStage) so pipeline
            // layouts stay supersets of the device's default bind groups.
            Assert.That(frameGroup.Bindings[0].Entry.Stage, Is.EqualTo(ShaderStage.Standard));
            Assert.That(frameGroup.Bindings[0].Size, Is.EqualTo(64u)); // float4x4

            BindGroupLayout materialGroup = reflection.BindGroups[1];
            Assert.That(materialGroup.Group, Is.EqualTo(1u));
            Assert.That(materialGroup.Bindings.Count, Is.EqualTo(2));
            Assert.That(materialGroup.Bindings[0].Entry.Name, Is.EqualTo("_albedo"));
            Assert.That(materialGroup.Bindings[0].Entry.Type, Is.EqualTo(BindingType.Texture));
            Assert.That(materialGroup.Bindings[1].Entry.Name, Is.EqualTo("_albedoSampler"));
            Assert.That(materialGroup.Bindings[1].Entry.Type, Is.EqualTo(BindingType.Sampler));

            // Vertex input: POSITION (float3) + TEXCOORD0 (float2) → stride 12 + 8 = 20
            Assert.That(reflection.VertexLayouts.Count, Is.EqualTo(1));
            Assert.That(reflection.VertexLayouts[0].Stride, Is.EqualTo(20u));
            Assert.That(reflection.VertexLayouts[0].Elements[0].Format, Is.EqualTo(VertexFormat.Float32x3));
            Assert.That(reflection.VertexLayouts[0].Elements[1].Format, Is.EqualTo(VertexFormat.Float32x2));

            Assert.That(reflection.FragmentOutputCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void Compile_ComputeShader_ReflectionCarriesThreadGroupSize()
    {
        using SlangCompiler compiler = SlangCompiler.Create();
        using SlangCompileSession session = compiler.CreateSession(SlangCompilerOptions.Default);
        SlangModuleHandle module = session.LoadModuleFromSource("alco_test_compute", "alco_test_compute.slang", ComputeShader);
        using SlangProgram program = session.Compile(module, [new SlangEntryPointRequest("MainCS", ShaderStage.Compute)]);

        Assert.Multiple(() =>
        {
            Assert.That(program.Reflection.Size, Is.EqualTo(new ThreadGroupSize(8, 4, 1)));
            Assert.That(program.Reflection.BindGroups.Count, Is.EqualTo(1));
            Assert.That(program.Reflection.BindGroups[0].Group, Is.EqualTo(0u));
            Assert.That(program.Reflection.BindGroups[0].Bindings[0].Entry.Type, Is.EqualTo(BindingType.StorageBuffer));
            Assert.That(program.Reflection.BindGroups[0].Bindings[0].Entry.Stage, Is.EqualTo(ShaderStage.Standard));
        });
    }

    [Test]
    public void LoadModule_ResolvesImportsThroughVirtualFileSystem()
    {
        Dictionary<string, string> files = new()
        {
            ["alco_test_lib.slang"] = ImportableLib,
        };

        using SlangCompiler compiler = SlangCompiler.Create();
        SlangCompilerOptions options = new()
        {
            Resolver = path => files.TryGetValue(path, out string? content) ? content : null,
            Exists = path => files.ContainsKey(path),
        };
        using SlangCompileSession session = compiler.CreateSession(options);

        SlangModuleHandle module = session.LoadModuleFromSource("alco_test_importing", "alco_test_importing.slang", ImportingShader);
        using SlangProgram program = session.Compile(module, [new SlangEntryPointRequest("MainPS", ShaderStage.Fragment)]);

        Assert.That(program.EntryCode[0].Length, Is.GreaterThan(4));
        // The import dependency must be visible through the module's dependency list.
        Assert.That(module.GetDependencyFilePaths(), Has.Some.Contains("alco_test_lib"));
    }

    [Test]
    public void LoadModule_SearchPathResolvesModuleByName()
    {
        Dictionary<string, string> files = new()
        {
            ["shaders/alco_by_name.slang"] = """
                [shader("fragment")]
                float4 MainPS() : SV_TARGET { return float4(1, 1, 0, 1); }
                """,
        };

        // Decisive experiment: back the virtual resolver with real files on
        // disk and compare against the pure-virtual variant below.
        string dir = Path.Combine(Path.GetTempPath(), $"alco_slang_test_{Guid.NewGuid():N}", "shaders");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "alco_by_name.slang"), files["shaders/alco_by_name.slang"]);

        try
        {
            using SlangCompiler compiler = SlangCompiler.Create();
            SlangCompilerOptions diskOptions = new()
            {
                SearchPaths = [dir.Replace('\\', '/')],
            };
            using SlangCompileSession diskSession = compiler.CreateSession(diskOptions);
            SlangModuleHandle diskModule = diskSession.LoadModule("alco_by_name");
            using SlangProgram diskProgram = diskSession.Compile(diskModule, [new SlangEntryPointRequest("MainPS", ShaderStage.Fragment)]);
            Assert.That(diskProgram.EntryCode[0].Length, Is.GreaterThan(4), "disk-backed search path must resolve the module");
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(dir)!, true);
        }

        using SlangCompiler compiler2 = SlangCompiler.Create();
        string[] searchRoots = ["shaders"];
        SlangCompilerOptions options = new()
        {
            SearchPaths = searchRoots,
            Resolver = path =>
            {
                // Search-path emulation on the managed side: try the path as
                // given, then under every search root.
                if (files.TryGetValue(SlangPathUtility.NormalizePath(path), out string? content))
                    return content;
                foreach (string root in searchRoots)
                {
                    if (files.TryGetValue(SlangPathUtility.NormalizePath($"{root}/{path}"), out content))
                        return content;
                }
                return null;
            },
            Exists = path => files.ContainsKey(SlangPathUtility.NormalizePath(path)),
        };
        using SlangCompileSession session = compiler2.CreateSession(options);

        SlangModuleHandle module = session.LoadModule("alco_by_name");
        using SlangProgram program = session.Compile(module, [new SlangEntryPointRequest("MainPS", ShaderStage.Fragment)]);
        Assert.That(program.EntryCode[0].Length, Is.GreaterThan(4));
    }

    [Test]
    public void ModuleIR_RoundTripsThroughSerializeAndLoadFromIRBlob()
    {
        using SlangCompiler compiler = SlangCompiler.Create();
        ShaderReflectionInfo fromSource;
        byte[] ir;
        using (SlangCompileSession session = compiler.CreateSession(SlangCompilerOptions.Default))
        {
            SlangModuleHandle module = session.LoadModuleFromSource("alco_ir_test", "alco_ir_test.slang", GraphicsShader);
            using SlangProgram program = session.Compile(module, [
                new SlangEntryPointRequest("MainVS", ShaderStage.Vertex),
                new SlangEntryPointRequest("MainPS", ShaderStage.Fragment)]);
            fromSource = program.Reflection;

            ir = module.Serialize()!;
            Assert.That(ir.Length, Is.GreaterThan(0), "module serialization produced an empty blob");
            Assert.That(session.IsBinaryModuleUpToDate("alco_ir_test.slang", ir), Is.True,
                "a serialized module must be up-to-date against unchanged sources");
        }

        // Restore in a fresh session and compile the same entry points.
        using SlangCompileSession restoreSession = compiler.CreateSession(SlangCompilerOptions.Default);
        SlangModuleHandle restored = restoreSession.LoadModuleFromIRBlob("alco_ir_test", "alco_ir_test.slang", ir);
        using SlangProgram restoredProgram = restoreSession.Compile(restored, [
            new SlangEntryPointRequest("MainVS", ShaderStage.Vertex),
            new SlangEntryPointRequest("MainPS", ShaderStage.Fragment)]);

        Assert.Multiple(() =>
        {
            Assert.That(restoredProgram.EntryCode[0].Length, Is.GreaterThan(4));
            Assert.That(restoredProgram.EntryCode[1].Length, Is.GreaterThan(4));
            Assert.That(restoredProgram.Reflection.BindGroups.Count, Is.EqualTo(fromSource.BindGroups.Count));
            Assert.That(restored.Name, Is.EqualTo("alco_ir_test"));
            // Dependency paths are a source-load-time artifact: IR-restored modules
            // report an empty list, so ShaderSystem persists the graph in the cache
            // sidecar instead of re-querying restored modules.
        });
    }

    [Test]
    public void Compile_InvalidShader_ReportsErrors()
    {
        using SlangCompiler compiler = SlangCompiler.Create();
        using SlangCompileSession session = compiler.CreateSession(SlangCompilerOptions.Default);

        Assert.Throws<ShaderCompilationException>(() =>
            session.LoadModuleFromSource("alco_test_invalid", "alco_test_invalid.slang", "this is not valid slang at all : : :"));
    }

    [Test]
    public void GetUniformMembers_ReturnsReflectedOffsets()
    {
        const string shader = """
            cbuffer _materialParams : register(b0, space0)
            {
                float baseColor;
                float2 tiling;
                float4 tint;
            };

            [shader("fragment")]
            float4 MainPS() : SV_TARGET { return tint * baseColor; }
            """;

        using SlangCompiler compiler = SlangCompiler.Create();
        using SlangCompileSession session = compiler.CreateSession(SlangCompilerOptions.Default);
        SlangModuleHandle module = session.LoadModuleFromSource("alco_test_params", "alco_test_params.slang", shader);
        using SlangProgram program = session.Compile(module, [new SlangEntryPointRequest("MainPS", ShaderStage.Fragment)]);

        List<SlangUniformMember> members = program.GetUniformMembers("_materialParams");
        Assert.Multiple(() =>
        {
            Assert.That(members.Count, Is.EqualTo(3));
            Assert.That(members[0].Name, Is.EqualTo("baseColor"));
            Assert.That(members[0].FloatComponentCount, Is.EqualTo(1));
            Assert.That(members[1].Name, Is.EqualTo("tiling"));
            Assert.That(members[1].FloatComponentCount, Is.EqualTo(2));
            Assert.That(members[2].Name, Is.EqualTo("tint"));
            Assert.That(members[2].FloatComponentCount, Is.EqualTo(4));
        });
    }
}
