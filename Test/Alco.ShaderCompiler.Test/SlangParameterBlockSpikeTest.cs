using System.Text;
using NUnit.Framework;

namespace Alco.ShaderCompiler;

// ─────────────────────────────────────────────────────────────────────────────
// Verified capability (Slang 2026.16, SPIR-V 1.3, engine session config):
// `ParameterBlock<T>` groups a struct's parameters into ONE descriptor set
// with sequential bindings — no register / [[vk::binding]] annotation needed.
// The SPIR-V DescriptorSet/Binding decorations are the ground truth (the
// reflection API reports sub-object-relative spaces for block members):
//
//   1. automatic layout: the entry module's blocks claim sets in declaration
//      order first, then imported modules' blocks; bare (non-block) globals
//      claim the implicit default set 0 before any block;
//   2. ordinary data in a block auto-introduces a uniform buffer at binding 0
//      and shifts the resource members after it — the same physical shape as
//      the set-scoped cbuffer convention;
//   3. unused members keep their layout slots (bindings are assigned before
//      dead-code elimination) but vanish from the compiled entry code;
//   4. register(bN, spaceM) on a block pins the set for the entry module's
//      own block but is IGNORED for an imported module's block;
//      [[vk::binding(b, s)]] pins reliably in both cases;
//   5. the reflection bridge (SlangReflectionReader) rejects the
//      PARAMETER_BLOCK kind today — adopting the construct needs a reader
//      branch that derives each member's absolute set (automatic layout: the
//      block's sub-object index; pinned layout: from the annotation), since
//      member-level reflection spaces are relative (always 0).
// ─────────────────────────────────────────────────────────────────────────────
[TestFixture]
public class SlangParameterBlockSpikeTest
{
    // The shared "core" module: a sampler-only block, no ordinary data.
    private const string CoreModule = """
        #language slang 2025
        module test_pb_core;

        public struct GlobalSamplers
        {
            public SamplerState linearClamp;
            public SamplerState linearRepeat;
            public SamplerComparisonState depthCompare;
        };

        public ParameterBlock<GlobalSamplers> globalSamplers;
        """;

    // The entry module: a mixed pass block (ordinary data + texture + sampler
    // + storage buffer). linearRepeat/depthCompare/_sceneSampler are never
    // used by the body — bindings are assigned before dead-code elimination.
    private const string EntryModule = """
        #language slang 2025
        module test_pb_entry;

        import test_pb_core;

        public struct PassParams
        {
            public float4 tint;
            public Texture2D sceneColor;
            public SamplerState sceneSampler;
            public RWStructuredBuffer<float4> output;
        };

        public ParameterBlock<PassParams> pass;

        [shader("fragment")]
        float4 MainPS(float2 uv : TEXCOORD0) : SV_TARGET
        {
            float4 color = pass.sceneColor.Sample(globalSamplers.linearClamp, uv) * pass.tint;
            pass.output[0] = color;
            return color;
        }
        """;

    // A bare cbuffer (no register annotation) next to a ParameterBlock.
    private const string MixedModule = """
        #language slang 2025
        module test_pb_mixed;

        import test_pb_core;

        cbuffer camera
        {
            float4x4 viewProjection;
        };

        ParameterBlock<PassParams> pass;

        public struct PassParams
        {
            public float4 tint;
            public Texture2D sceneColor;
        };

        [shader("fragment")]
        float4 MainPS(float2 uv : TEXCOORD0) : SV_TARGET
        {
            return pass.sceneColor.Sample(globalSamplers.linearClamp, uv) * pass.tint
                 + viewProjection._m00;
        }
        """;

    // Pinned blocks: the engine's per-module set ownership expressed with
    // ParameterBlock + register — core owns space0, the entry module space1.
    private const string PinnedCoreModule = """
        #language slang 2025
        module test_pb_pinned_core;

        public struct GlobalSamplers
        {
            public SamplerState linearClamp;
            public SamplerState linearRepeat;
            public SamplerComparisonState depthCompare;
        };

        public ParameterBlock<GlobalSamplers> globalSamplers : register(b0, space0);
        """;

    private const string PinnedEntryModule = """
        #language slang 2025
        module test_pb_pinned_entry;

        import test_pb_pinned_core;

        public struct PassParams
        {
            public float4 tint;
            public Texture2D sceneColor;
            public SamplerState sceneSampler;
            public RWStructuredBuffer<float4> output;
        };

        public ParameterBlock<PassParams> pass : register(b0, space1);

        [shader("fragment")]
        float4 MainPS(float2 uv : TEXCOORD0) : SV_TARGET
        {
            float4 color = pass.sceneColor.Sample(globalSamplers.linearClamp, uv) * pass.tint;
            pass.output[0] = color;
            return color;
        }
        """;

    // vk::binding-pinned blocks: does the Vulkan-native annotation pin the
    // block's set where register could not?
    private const string VkPinnedEntryModule = """
        #language slang 2025
        module test_pb_vk_entry;

        import test_pb_core;

        public struct VkPassParams
        {
            public float4 tint;
            public Texture2D sceneColor;
            public RWStructuredBuffer<float4> output;
        };

        [[vk::binding(0, 1)]] ParameterBlock<VkPassParams> pass;
        [[vk::binding(0, 0)]] ParameterBlock<GlobalSamplers> globalSamplers2;

        [shader("fragment")]
        float4 MainPS(float2 uv : TEXCOORD0) : SV_TARGET
        {
            float4 color = pass.sceneColor.Sample(globalSamplers2.linearClamp, uv) * pass.tint;
            pass.output[0] = color;
            return color;
        }
        """;

    // Scanner baseline: the engine's current cbuffer+register convention must
    // land exactly where the reflection reader says it does.
    private const string CbufferBaselineModule = """
        #language slang 2025
        module test_pb_baseline;

        cbuffer pass : register(b0, space1)
        {
            float4 tint;
            Texture2D sceneColor;
            SamplerState sceneSampler;
        };

        cbuffer frame : register(b0, space0)
        {
            float4 time;
        };

        [shader("fragment")]
        float4 MainPS(float2 uv : TEXCOORD0) : SV_TARGET
        {
            return sceneColor.Sample(sceneSampler, uv) * tint * time;
        }
        """;

    [Test]
    public void VkBindingPinnedParameterBlocks_KeepOneSetPerBlock()
    {
        (byte[][] code, _) = CompileAndDump("test_pb_vk_entry", VkPinnedEntryModule);

        // vk::binding on the block pins its set exactly; members number from
        // the auto-introduced uniform buffer (binding 0) in declaration order.
        Assert.That(Rows(code[0]), Is.EqualTo(new[]
        {
            (0u, 0u, "globalSamplers2.linearClamp"),
            (1u, 0u, "pass"),
            (1u, 1u, "pass.sceneColor"),
            (1u, 2u, "pass.output"),
        }));
    }

    [Test]
    public void CbufferRegisterBaseline_ScannerAgreesWithReader()
    {
        (byte[][] code, string dump) = CompileAndDump("test_pb_baseline", CbufferBaselineModule);
        TestContext.Progress.WriteLine(dump);

        // The scanner reads the same layout the reflection reader rebases:
        // block at register(b0, space1) owns set 1 — UBO at binding 0,
        // resource members continue in declaration order.
        Assert.That(Rows(code[0]), Is.EqualTo(new[]
        {
            (0u, 0u, "frame"),
            (1u, 0u, "pass"),
            (1u, 1u, "pass.sceneColor"),
            (1u, 2u, "pass.sceneSampler"),
        }));
    }

    [Test]
    public void ParameterBlocksAcrossModules_ReflectAutoSetsAndBindings()
    {
        (byte[][] code, string dump) = CompileAndDump("test_pb_entry", EntryModule);
        TestContext.Progress.WriteLine(dump);

        // Auto layout, as described at the top of this file.
        Assert.That(code[0][0..4], Is.EqualTo(new byte[] { 0x03, 0x02, 0x23, 0x07 }),
            "the entry must be SPIR-V");
        Assert.That(Rows(code[0]), Is.EqualTo(new[]
        {
            (0u, 0u, "pass"),
            (0u, 1u, "pass.sceneColor"),
            (0u, 3u, "pass.output"),
            (1u, 0u, "globalSamplers.linearClamp"),
        }));
    }

    [Test]
    public void PinnedParameterBlocks_KeepOneSetPerBlock()
    {
        (byte[][] code, string dump) = CompileAndDump("test_pb_pinned_entry", PinnedEntryModule, PinnedCoreModule);
        TestContext.Progress.WriteLine(dump);

        // Pinned-layout caveat, as described at the top of this file.
        List<(uint Set, uint Binding, string Name)> rows = Rows(code[0]);
        Assert.That(rows, Does.Contain((1u, 0u, "pass")));
        Assert.That(rows, Does.Contain((1u, 1u, "pass.sceneColor")));
        Assert.That(rows, Does.Contain((1u, 3u, "pass.output")));
        Assert.That(rows.Where(row => row.Name == "globalSamplers.linearClamp").Single().Set,
            Is.Not.EqualTo(0u), "the imported module's register(space0) pin must not hold");
    }

    [Test]
    public void BareCbufferMixedWithParameterBlock_ReflectsDefaultSet()
    {
        (byte[][] code, string dump) = CompileAndDump("test_pb_mixed", MixedModule);
        TestContext.Progress.WriteLine(dump);

        // Bare (non-block) globals claim the implicit default set 0 first;
        // explicit parameter blocks then number from set 1 upward.
        Assert.That(Rows(code[0]), Is.EqualTo(new[]
        {
            (0u, 0u, "camera"),
            (1u, 0u, "pass"),
            (1u, 1u, "pass.sceneColor"),
            (2u, 0u, "globalSamplers.linearClamp"),
        }));
    }

    /// <summary>
    /// Ground truth: the DescriptorSet/Binding decorations baked into the
    /// compiled SPIR-V — what wgpu's pipeline layout must match, regardless
    /// of how the reflection API phrases the block hierarchy.
    /// </summary>
    private static List<(uint Set, uint Binding, string Name)> Rows(byte[] spirv)
    {
        uint[] words = new uint[spirv.Length / 4];
        Buffer.BlockCopy(spirv, 0, words, 0, words.Length * 4);
        if (words[0] != 0x07230203u)
        {
            throw new AssertionException("the blob is not SPIR-V");
        }

        Dictionary<uint, string> names = [];
        Dictionary<uint, (uint? Set, uint? Binding)> decorations = [];
        for (int i = 5; i < words.Length;)
        {
            uint opcode = words[i] & 0xFFFF;
            uint wordCount = words[i] >> 16;
            if (wordCount == 0)
            {
                break;
            }

            if (opcode == 5) // OpName
            {
                names[words[i + 1]] = ReadSpirvString(words, i + 2, (int)wordCount - 2);
            }
            else if (opcode == 71) // OpDecorate
            {
                uint target = words[i + 1];
                uint decoration = words[i + 2];
                uint? rawOperand = wordCount >= 4 ? words[i + 3] : null;
                (uint? set, uint? binding) = decorations.GetValueOrDefault(target);
                if (decoration == 33) // Binding
                {
                    binding = rawOperand;
                }
                else if (decoration == 34) // DescriptorSet
                {
                    set = rawOperand;
                }

                decorations[target] = (set, binding);
            }

            i += (int)wordCount;
        }

        List<(uint Set, uint Binding, string Name)> rows = [];
        foreach (KeyValuePair<uint, (uint? Set, uint? Binding)> pair in decorations)
        {
            if (pair.Value.Set is uint set && pair.Value.Binding is uint binding)
            {
                rows.Add((set, binding, names.GetValueOrDefault(pair.Key)));
            }
        }

        rows.Sort((left, right) => left.Set != right.Set ? left.Set.CompareTo(right.Set) : left.Binding.CompareTo(right.Binding));
        return rows;
    }

    private static string ReadSpirvString(uint[] words, int start, int wordCount)
    {
        byte[] bytes = new byte[wordCount * 4];
        for (int w = 0; w < wordCount; w++)
        {
            uint word = words[start + w];
            bytes[w * 4 + 0] = (byte)(word & 0xFF);
            bytes[w * 4 + 1] = (byte)((word >> 8) & 0xFF);
            bytes[w * 4 + 2] = (byte)((word >> 16) & 0xFF);
            bytes[w * 4 + 3] = (byte)((word >> 24) & 0xFF);
        }

        int length = Array.IndexOf(bytes, (byte)0);
        return System.Text.Encoding.UTF8.GetString(bytes, 0, length < 0 ? bytes.Length : length);
    }

    // Links the module like SlangCompileSession.Compile does, but skips
    // BuildReflectionInfo (which rejects the PARAMETER_BLOCK kind today) and
    // dumps the raw ProgramLayout instead.
    private static (byte[][] Code, string Dump) CompileAndDump(string name, string source, string? coreModule = null)
    {
        string core = coreModule ?? CoreModule;
        Dictionary<string, string> files = new()
        {
            ["test_pb_core.slang"] = core,
            ["test_pb_pinned_core.slang"] = core,
            [$"{name}.slang"] = source,
        };
        SlangCompilerOptions options = new()
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

        using SlangCompiler compiler = new SlangCompiler();
        using SlangCompileSession session = compiler.CreateSession(options);
        SlangModuleHandle module = session.LoadModuleFromSource(name, $"{name}.slang", source);

        SlangModule native = module.Native;
        int count = native.DefinedEntryPointCount;
        SlangComponentType[] components = new SlangComponentType[count + 1];
        components[0] = native.AsComponentType();
        for (int i = 0; i < count; i++)
        {
            components[i + 1] = native.GetDefinedEntryPoint(i)!.AsComponentType();
        }

        try
        {
            SlangComponentType composite = session.Native.CreateCompositeComponentType(components, out _);
            try
            {
                SlangComponentType linked = composite.Link(out string? linkDiagnostics);
                try
                {
                    Assert.That(linkDiagnostics, Is.Null, $"slang link diagnostics: {linkDiagnostics}");
                    IntPtr layout = linked.GetLayout(out string? layoutDiagnostics);
                    Assert.That(layout, Is.Not.EqualTo(IntPtr.Zero), $"getLayout failed: {layoutDiagnostics}");

                    byte[][] code = new byte[count][];
                    for (int i = 0; i < count; i++)
                    {
                        code[i] = linked.GetEntryPointCode(i, out _);
                    }

                    return (code, DumpLayout(layout));
                }
                finally
                {
                    linked.Release();
                }
            }
            finally
            {
                composite.Release();
            }
        }
        finally
        {
            for (int i = 1; i < components.Length; i++)
            {
                components[i]?.Release();
            }
        }
    }

    private static string DumpLayout(IntPtr layout)
    {
        StringBuilder sb = new();
        uint parameterCount = SlangNative.spReflection_GetParameterCount(layout);
        sb.AppendLine($"global parameters: {parameterCount}");
        for (uint i = 0; i < parameterCount; i++)
        {
            IntPtr parameter = SlangNative.spReflection_GetParameterByIndex(layout, i);
            if (parameter == IntPtr.Zero)
            {
                continue;
            }

            string? name = LayoutName(parameter);
            IntPtr typeLayout = SlangNative.spReflectionVariableLayout_GetTypeLayout(parameter);
            int kind = SlangNative.spReflectionTypeLayout_getKind(typeLayout);
            uint space = SlangNative.spReflectionParameter_GetBindingSpace(parameter);
            uint binding = SlangNative.spReflectionParameter_GetBindingIndex(parameter);
            sb.AppendLine($"  [{i}] '{name}' kind={kind} space={space} binding={binding}");

            if (kind == SlangNative.SLANG_TYPE_KIND_PARAMETER_BLOCK ||
                kind == SlangNative.SLANG_TYPE_KIND_CONSTANT_BUFFER)
            {
                IntPtr element = SlangNative.spReflectionTypeLayout_GetElementTypeLayout(typeLayout);
                uint uniformSize = (uint)SlangNative.spReflectionTypeLayout_GetSize(
                    element, SlangNative.SLANG_PARAMETER_CATEGORY_UNIFORM);
                sb.AppendLine($"      element uniform size: {uniformSize}");
                DumpFields(element, "      ", sb);
            }
        }

        return sb.ToString();
    }

    private static void DumpFields(IntPtr structLayout, string indent, StringBuilder sb)
    {
        uint fieldCount = SlangNative.spReflectionTypeLayout_GetFieldCount(structLayout);
        sb.AppendLine($"{indent}fields: {fieldCount}");
        for (uint field = 0; field < fieldCount; field++)
        {
            IntPtr fieldLayout = SlangNative.spReflectionTypeLayout_GetFieldByIndex(structLayout, field);
            if (fieldLayout == IntPtr.Zero)
            {
                continue;
            }

            string? fieldName = LayoutName(fieldLayout);
            IntPtr fieldTypeLayout = SlangNative.spReflectionVariableLayout_GetTypeLayout(fieldLayout);
            int fieldKind = SlangNative.spReflectionTypeLayout_getKind(fieldTypeLayout);
            uint relativeBinding = SlangNative.spReflectionParameter_GetBindingIndex(fieldLayout);
            uint fieldSpace = SlangNative.spReflectionParameter_GetBindingSpace(fieldLayout);
            IntPtr type = SlangNative.spReflectionTypeLayout_GetType(fieldTypeLayout);
            string? typeName = SlangNative.StringFromPtr(SlangNative.spReflectionType_GetName(type));
            sb.AppendLine($"{indent}- '{fieldName}' kind={fieldKind} type='{typeName}' relativeBinding={relativeBinding} space={fieldSpace}");
        }
    }

    // The material-path shape: a pass template (two blocks + generic entry
    // points) composed with a surface module (one block) through slang's
    // component system — the path GetComposedProgram/MaterialCompiler uses.
    private const string TemplateModule = """
        #language slang 2025
        module test_pb_template;

        import test_pb_core;

        public struct CameraParams { public float4x4 viewProjection; };
        public struct DrawParams { public RWStructuredBuffer<float4> instances; };

        public ParameterBlock<CameraParams> camera;
        public ParameterBlock<DrawParams> draw;

        public struct TmplV2F
        {
            public float4 position : SV_POSITION;
        };

        [shader("vertex")]
        public TmplV2F MainVS(float3 position : POSITION)
        {
            TmplV2F output;
            output.position = mul(camera.viewProjection, float4(position, 1.0)) + draw.instances[0];
            return output;
        }
        """;

    private const string CompanionSurfaceModule = """
        #language slang 2025
        module test_pb_surface;

        public struct MaterialParams
        {
            public Texture2D<float4> albedoTexture;
            public SamplerState albedoTextureSampler;
        };

        public ParameterBlock<MaterialParams> material;
        """;

    [Test]
    public void ComposedProgram_EntryBlocksFirstThenCompanion()
    {
        // Composition mirrors the composed-program path: components[0] = the template
        // module, components[1] = the companion (surface) module, then the
        // template's entry points.
        Dictionary<string, string> files = new()
        {
            ["test_pb_core.slang"] = CoreModule,
            ["test_pb_template.slang"] = TemplateModule,
            ["test_pb_surface.slang"] = CompanionSurfaceModule,
        };
        SlangCompilerOptions options = OptionsFor(files);

        using SlangCompiler compiler = new SlangCompiler();
        using SlangCompileSession session = compiler.CreateSession(options);
        SlangModuleHandle template = session.LoadModuleFromSource(
            "test_pb_template", "test_pb_template.slang", TemplateModule);
        SlangModuleHandle companion = session.LoadModuleFromSource(
            "test_pb_surface", "test_pb_surface.slang", CompanionSurfaceModule);

        SlangModule native = template.Native;
        SlangEntryPoint ep = native.GetDefinedEntryPoint(0)!;
        SlangComponentType[] components =
        [
            native.AsComponentType(),
            companion.Native.AsComponentType(),
            ep.AsComponentType(),
        ];

        SlangComponentType composite = session.Native.CreateCompositeComponentType(components, out _);
        try
        {
            SlangComponentType linked = composite.Link(out string? linkDiagnostics);
            try
            {
                Assert.That(linkDiagnostics, Is.Null, $"slang link diagnostics: {linkDiagnostics}");
                IntPtr layout = linked.GetLayout(out string? layoutDiagnostics);
                Assert.That(layout, Is.Not.EqualTo(IntPtr.Zero), $"getLayout failed: {layoutDiagnostics}");
                byte[] code = linked.GetEntryPointCode(0, out _);

                List<(uint Set, uint Binding, string Name)> rows = Rows(code);
                TestContext.Progress.WriteLine(string.Join("\n", rows.Select(r => $"  set={r.Set} binding={r.Binding}  '{r.Name}'")));

                // Reader-derivation truth: block binding index = absolute set,
                // member binding index = absolute binding in the set (shifted
                // past the auto-UBO). Must match the SPIR-V decorations of the
                // entry-referenced resources exactly.
                (uint Set, uint Binding, string Name)[] layoutRows = GetOffsetSets(layout);
                TestContext.Progress.WriteLine(string.Join("\n", layoutRows.Select(r => $"  set={r.Set} binding={r.Binding}  '{r.Name}'")));
                Assert.That(layoutRows, Is.EqualTo(new[]
                {
                    (0u, 0u, "camera"),
                    (1u, 0u, "draw.instances"),
                    (2u, 0u, "material.albedoTexture"),
                    (2u, 1u, "material.albedoTextureSampler"),
                    (3u, 0u, "globalSamplers.linearClamp"),
                    (3u, 1u, "globalSamplers.linearRepeat"),
                    (3u, 2u, "globalSamplers.depthCompare"),
                }), "block binding index = set; member binding index = binding in set");

                // Entry-code truth: only the resources the entry actually
                // references carry SPIR-V decorations; the unused companion
                // block is DCE-stripped from the code but keeps its layout
                // slot (bindings are assigned before dead-code elimination).
                // The used blocks' SPIR-V sets equal their parameter order.
                Assert.That(rows, Is.EqualTo(new[]
                {
                    (0u, 0u, "camera"),
                    (1u, 0u, "draw.instances"),
                }));
            }
            finally
            {
                linked.Release();
            }
        }
        finally
        {
            composite.Release();
            ep.Release();
        }
    }

    /// <summary>
    /// The reader-side derivation, cross-checked against the SPIR-V
    /// decorations in the tests above: under automatic layout a global
    /// PARAMETER_BLOCK parameter's binding index IS its absolute descriptor
    /// set (GetBindingSpace reports the explicit register annotation only, 0
    /// under auto layout), and a member's binding index is its absolute
    /// binding inside that set — already shifted past the block's
    /// automatically-introduced uniform buffer (binding 0).
    /// </summary>
    private static (uint Set, uint Binding, string Name)[] GetOffsetSets(IntPtr layout)
    {
        List<(uint, uint, string)> result = [];
        uint parameterCount = SlangNative.spReflection_GetParameterCount(layout);
        for (uint i = 0; i < parameterCount; i++)
        {
            IntPtr parameter = SlangNative.spReflection_GetParameterByIndex(layout, i);
            if (parameter == IntPtr.Zero)
            {
                continue;
            }

            string? name = LayoutName(parameter);
            IntPtr typeLayout = SlangNative.spReflectionVariableLayout_GetTypeLayout(parameter);
            int kind = SlangNative.spReflectionTypeLayout_getKind(typeLayout);
            if (kind != SlangNative.SLANG_TYPE_KIND_PARAMETER_BLOCK)
            {
                continue;
            }

            uint set = SlangNative.spReflectionParameter_GetBindingIndex(parameter);
            IntPtr element = SlangNative.spReflectionTypeLayout_GetElementTypeLayout(typeLayout);
            uint uniformSize = (uint)SlangNative.spReflectionTypeLayout_GetSize(
                element, SlangNative.SLANG_PARAMETER_CATEGORY_UNIFORM);
            if (uniformSize > 0)
            {
                result.Add((set, 0u, name ?? "?"));
            }

            DumpFieldsInto(element, set, name ?? "?", result);
        }

        result.Sort((left, right) => left.Item1 != right.Item1
            ? left.Item1.CompareTo(right.Item1)
            : left.Item2.CompareTo(right.Item2));
        return result.ToArray();
    }

    private static void DumpFieldsInto(
        IntPtr structLayout, uint set, string prefix, List<(uint, uint, string)> result)
    {
        uint fieldCount = SlangNative.spReflectionTypeLayout_GetFieldCount(structLayout);
        for (uint field = 0; field < fieldCount; field++)
        {
            IntPtr fieldLayout = SlangNative.spReflectionTypeLayout_GetFieldByIndex(structLayout, field);
            if (fieldLayout == IntPtr.Zero)
            {
                continue;
            }

            string? fieldName = LayoutName(fieldLayout);
            IntPtr fieldTypeLayout = SlangNative.spReflectionVariableLayout_GetTypeLayout(fieldLayout);
            int fieldKind = SlangNative.spReflectionTypeLayout_getKind(fieldTypeLayout);
            if (fieldKind is SlangNative.SLANG_TYPE_KIND_SAMPLER_STATE
                or SlangNative.SLANG_TYPE_KIND_RESOURCE
                or SlangNative.SLANG_TYPE_KIND_SHADER_STORAGE_BUFFER)
            {
                uint binding = SlangNative.spReflectionParameter_GetBindingIndex(fieldLayout);
                result.Add((set, binding, $"{prefix}.{fieldName}"));
            }
        }
    }

    [Test]
    public void EngineReflectionReader_BuildsBindGroupsFromParameterBlocks()
    {
        // End-to-end through the engine's normal path: SlangCompileSession.Compile
        // runs BuildReflectionInfo, which must flatten each block into one bind
        // group with bare member names (same contract as set-scoped cbuffers).
        SlangCompilerOptions options = OptionsFor(new Dictionary<string, string>
        {
            ["test_pb_core.slang"] = CoreModule,
            ["test_pb_entry.slang"] = EntryModule,
        });
        using SlangCompiler compiler = new SlangCompiler();
        using SlangCompileSession session = compiler.CreateSession(options);
        SlangModuleHandle module = session.LoadModuleFromSource("test_pb_entry", "test_pb_entry.slang", EntryModule);
        using SlangProgram program = session.Compile(module, [new SlangEntryPointRequest("MainPS", Alco.Graphics.ShaderStage.Fragment)]);

        Alco.Graphics.ShaderReflection reflection = program.Reflection;
        Assert.That(reflection.BindGroups.Count, Is.EqualTo(2));

        Alco.Graphics.BindGroupLayout pass = reflection.BindGroups[0];
        Assert.That(pass.Group, Is.EqualTo(0u));
        Assert.That(pass.Bindings.Select(b => (b.Entry.Name, b.Entry.Binding, b.Entry.Type)), Is.EqualTo(new[]
        {
            ("pass", 0u, Alco.Graphics.BindingType.UniformBuffer),      // auto-introduced UBO (tint)
            ("sceneColor", 1u, Alco.Graphics.BindingType.Texture),      // bare name, shifted past the UBO
            ("sceneSampler", 2u, Alco.Graphics.BindingType.Sampler),    // unused by the body, kept pre-DCE
            ("output", 3u, Alco.Graphics.BindingType.StorageBuffer),
        }));

        Alco.Graphics.BindGroupLayout samplers = reflection.BindGroups[1];
        Assert.That(samplers.Group, Is.EqualTo(1u));
        Assert.That(samplers.Bindings.Select(b => (b.Entry.Name, b.Entry.Binding, b.Entry.Type)), Is.EqualTo(new[]
        {
            ("linearClamp", 0u, Alco.Graphics.BindingType.Sampler),
            ("linearRepeat", 1u, Alco.Graphics.BindingType.Sampler),
            ("depthCompare", 2u, Alco.Graphics.BindingType.SamplerComparison),
        }));
        Assert.That(reflection.TryGetResourceId("sceneColor", out _), Is.True,
            "members resolve by their bare names");
    }

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

    private static string? LayoutName(IntPtr variableLayout)
    {
        IntPtr variable = SlangNative.spReflectionVariableLayout_GetVariable(variableLayout);
        return variable == IntPtr.Zero
            ? null
            : SlangNative.StringFromPtr(SlangNative.spReflectionVariable_GetName(variable));
    }
}
