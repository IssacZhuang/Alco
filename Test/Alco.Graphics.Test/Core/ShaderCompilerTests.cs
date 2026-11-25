// Unit tests for the official Gpu.Shaders compiler wrapper, virtual FS, reflection, ABI checks, caching, and binary serialization.

using System.Runtime.InteropServices;
using Alco.Graphics;
using Alco.Graphics.LowLevel;

internal static class ShaderCompilerTests
{
    private const string EmbeddedIncludeSource = """
        import Core;

        [__AttributeUsage(_AttributeTargets.Struct)]
        struct StructTagAttribute
        {
            string Label;
            int Value;
            float Scale;
        };

        [__AttributeUsage(_AttributeTargets.Var)]
        struct TagAttribute
        {
            string Label;
            int Value;
            float Scale;
        };

        [__AttributeUsage(_AttributeTargets.Function)]
        struct FuncTagAttribute
        {
            string Label;
            int Value;
            float Scale;
        };

        struct NestedData
        {
            float3 Color;
            uint Flags;
        };

        [GPURoot]
        [StructTag("root-struct", 42, 2.5)]
        struct SampleRoot
        {
            GPUBufferView Output;
            GPUTextureId Albedo;
            GPUSamplerId Sampler;
            [Tag("weight-field", 3, 1.25)]
            float Weight;
            float2x3 Transform;
            NestedData Nested;
        };

        interface IShadingModel
        {
            float Eval(float x);
        };

        struct MatteModel : IShadingModel
        {
            float Eval(float x) { return x * 0.5; }
        };

        struct GlossModel : IShadingModel
        {
            float Eval(float x) { return x * 2.0; }
        };

        [shader("compute")]
        [numthreads(8, 4, 2)]
        [FuncTag("entry-tag", 9, 0.5)]
        void computeMain<TModel : IShadingModel>(uint3 tid : SV_DispatchThreadID)
        {
            SampleRoot root = gpu_load_root<SampleRoot>();
            TModel model;
            float v = model.Eval(root.Weight);
            gpu_store<float>(root.Output, 0, v + float(tid.x) * 0.0);
        }
        """;

    [StructLayout(LayoutKind.Sequential)]
    private struct ExpectedNestedData
    {
        public float R;
        public float G;
        public float B;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExpectedSampleRoot
    {
        public GPUBufferView Output;
        public uint Albedo;
        public uint Sampler;
        public float Weight;
        public float M00;
        public float M01;
        public float M02;
        public float M10;
        public float M11;
        public float M12;
        public ExpectedNestedData Nested;
    }

    internal static void Run(Action<bool, string> check)
    {
        Console.WriteLine("Gpu.Shaders compiler wrapper, reflection, ABI, serialization, and disk cache:");

        check(ShaderCompiler.TargetForBackend(GraphicsBackend.Vulkan) == ShaderTarget.Spirv &&
            ShaderCompiler.TargetForBackend(GraphicsBackend.VulkanCompact) == ShaderTarget.SpirvCompact &&
            ShaderCompiler.TargetForBackend(GraphicsBackend.Dx12) == ShaderTarget.Dxil &&
            ShaderCompiler.TargetForBackend(GraphicsBackend.Metal) == ShaderTarget.MetalSource,
            "ShaderCompiler.TargetForBackend maps every Alco.Graphics backend to its official shader target");

        check(ShaderCompiler.IsTargetSupported(ShaderTarget.Spirv) &&
            ShaderCompiler.IsTargetSupported(ShaderTarget.SpirvCompact) &&
            ShaderCompiler.IsTargetSupported(ShaderTarget.MetalSource) &&
            (!OperatingSystem.IsWindows() || ShaderCompiler.IsTargetSupported(ShaderTarget.Dxil)),
            "ShaderCompiler.IsTargetSupported reports host support accurately");

        check(ShaderCompiler.GetCompiledEntryPointName(ShaderTarget.Spirv, "computeMain") == "main" &&
            ShaderCompiler.GetCompiledEntryPointName(ShaderTarget.SpirvCompact, "meshMain") == "main" &&
            ShaderCompiler.GetCompiledEntryPointName(ShaderTarget.Dxil, "computeMain") == "computeMain" &&
            ShaderCompiler.GetCompiledEntryPointName(ShaderTarget.MetalSource, "vertexMain") == "vertexMain" &&
            ShaderCompiler.GetCompiledEntryPointName(ShaderTarget.MetalLibrary, "taskMain") == "taskMain",
            "ShaderCompiler.GetCompiledEntryPointName normalizes SPIR-V main while preserving DXIL and Metal names");

        TestEmbeddedIncludeReflectionAndLifetime(check);
        TestZeroEntryPipelineSelection(check);
        TestAbiValidation(check);
        TestSpecializedRoots(check);
        TestSelectedEntryComposition(check);
        TestSerializationAndDiskCache(check);
        TestL1IrCacheRoundTripAndParity(check);
        TestComposedIrAndZeroSessionWarmStart(check);
        TestMultiTargetCacheIsolation(check);
        TestCacheStalenessAndCorruptionSafety(check);
    }

    private static void TestEmbeddedIncludeReflectionAndLifetime(Action<bool, string> check)
    {
        CompiledShader compiled;
        IReadOnlyList<ShaderModuleEntryPoint> moduleEntries;
        IReadOnlyList<string> implementations;
        IReadOnlyList<string> moduleDeps;

        using (ShaderCompiler compiler = new(ShaderTarget.Spirv, new ShaderCompilerOptions { SearchPaths = [] }))
        {
            check(!string.IsNullOrWhiteSpace(compiler.CompilerVersionTag), "ShaderCompiler exposes non-empty CompilerVersionTag");
            ShaderModule module = compiler.LoadModuleFromSource("embedded_test", "embedded_test.slang", EmbeddedIncludeSource);
            moduleEntries = module.EntryPoints;
            moduleDeps = module.DependencyPaths;
            implementations = module.FindImplementations("IShadingModel");

            check(moduleEntries.Count == 1 && moduleEntries[0].Name == "computeMain" &&
                moduleEntries[0].Stage == ShaderStageKind.Compute && moduleEntries[0].HasGenericParameters,
                "ShaderModule discovers entry point stage and generic specialization flag");
            check(moduleDeps.Count > 0, "ShaderModule reports non-empty dependency paths while excluding internal virtual prefix");
            check(implementations.Count == 2 && implementations.Contains("MatteModel") && implementations.Contains("GlossModel"),
                "ShaderModule.FindImplementations lists concrete structs implementing Slang interface");

            compiled = module.Compile(ShaderPipelineKind.Compute, new ShaderCompileOptions
            {
                SpecializationArgs = ["MatteModel"],
            });
        }

        // Verify CompiledShader and ShaderReflection remain fully readable after ShaderCompiler.Dispose().
        check(compiled.ModuleName == "embedded_test" && compiled.Target == ShaderTarget.Spirv && compiled.Stages.Count == 1,
            "CompiledShader survives ShaderCompiler disposal intact");

        ShaderStage gpuStage = compiled.GetGpuStage(ShaderStageKind.Compute);
        check(gpuStage.EntryPoint == "main" &&
            gpuStage.ThreadgroupSize.X == 8 && gpuStage.ThreadgroupSize.Y == 4 && gpuStage.ThreadgroupSize.Z == 2 &&
            !gpuStage.Code.IsEmpty,
            "CompiledShader.GetGpuStage resolves entry point name, code, and threadgroup dimensions");

        ShaderReflection reflection = compiled.Reflection;
        ShaderRootLayout? root = reflection.Root;
        check(root != null && root.TypeName == "SampleRoot" && root.MatchesSize<ExpectedSampleRoot>(),
            $"ShaderReflection.Root mirrors SampleRoot size ({root?.SizeBytes} == {Marshal.SizeOf<ExpectedSampleRoot>()})");
        check(reflection.MatchesStructLayout<ExpectedNestedData>("NestedData"),
            "ShaderReflection.MatchesStructLayout<T> validates nested struct size");

        ShaderFieldLayout? albedo = root?.Find("Albedo");
        ShaderFieldLayout? sampler = root?.Find("Sampler");
        ShaderFieldLayout? output = root?.Find("Output");
        ShaderFieldLayout? weight = root?.Find("Weight");
        ShaderFieldLayout? transform = root?.Find("Transform");
        ShaderFieldLayout? nestedColor = root?.Find("Nested.Color");
        ShaderFieldLayout? nestedFlags = root?.Find("Nested.Flags");

        check(output?.Semantic == ShaderFieldSemantic.BufferView &&
            albedo?.Semantic == ShaderFieldSemantic.TextureId &&
            sampler?.Semantic == ShaderFieldSemantic.SamplerId &&
            weight?.Semantic == ShaderFieldSemantic.Data,
            "ShaderFieldLayout classifies GPUBufferView, GPUTextureId, GPUSamplerId, and ordinary data semantics");

        check(weight != null && weight.OffsetBytes == (uint)Marshal.OffsetOf<ExpectedSampleRoot>(nameof(ExpectedSampleRoot.Weight)) &&
            weight.Attributes.Count == 1 && weight.Attributes[0].Name == "Tag" &&
            weight.Attributes[0].Arguments.Count == 3 &&
            weight.Attributes[0].Arguments[0].StringValue == "weight-field" &&
            weight.Attributes[0].Arguments[1].IntValue == 3 &&
            MathF.Abs(weight.Attributes[0].Arguments[2].FloatValue - 1.25f) < 1e-4f,
            "ShaderFieldLayout exposes exact offset and parsed string/int/float user attribute arguments");

        check(transform is
            {
                Type:
                {
                    Kind: ShaderTypeKind.Matrix,
                    ScalarType: ShaderScalarType.Float32,
                    MatrixRows: 2,
                    MatrixColumns: 3,
                    MatrixLayout: ShaderMatrixLayout.RowMajor,
                    SizeBytes: 24,
                },
            },
            "ShaderTypeLayout reflects matrix scalar type, row/column dimensions, row-major layout, and byte size");

        check(nestedColor != null && nestedFlags != null &&
            nestedColor.Type.Kind == ShaderTypeKind.Vector && nestedColor.Type.VectorLength == 3 &&
            nestedFlags.OffsetBytes == (uint)Marshal.OffsetOf<ExpectedSampleRoot>(nameof(ExpectedSampleRoot.Nested)) +
                (uint)Marshal.OffsetOf<ExpectedNestedData>(nameof(ExpectedNestedData.Flags)),
            "ShaderRootLayout.Find traverses dotted paths and accumulates nested byte offsets");

        ShaderTypeLayout? sampleType = reflection.FindType("SampleRoot");
        var structTag = sampleType?.Attributes.FirstOrDefault(attr => attr.Name == "StructTag");
        check(sampleType != null && sampleType.AlignmentBytes > 0 &&
            sampleType.Attributes.Any(attr => attr.Name == "GPURoot") &&
            structTag != null && structTag.Arguments[1].IntValue == 42,
            "ShaderReflection.FindType includes module struct declarations with alignment and attributes");

        ShaderEntryPointReflection? entry = reflection.FindEntryPoint("computeMain");
        check(entry != null && entry.Stage == ShaderStageKind.Compute &&
            entry.ThreadgroupSize.Equals(new Extent3D(8, 4, 2)) &&
            entry.Inputs.Count == 1 && string.Equals(entry.Inputs[0].SemanticName, "SV_DispatchThreadID", StringComparison.OrdinalIgnoreCase) &&
            entry.Attributes.Count == 1 && entry.Attributes[0].Name == "FuncTag" &&
            entry.GenericParameters.Count == 1 && entry.GenericParameters[0].Name == "TModel" &&
            entry.GenericParameters[0].IsTypeParameter &&
            entry.GenericParameters[0].ConstraintTypeName == "IShadingModel",
            "ShaderEntryPointReflection captures stage inputs/semantics, threadgroup size, attributes, and generic parameters");
    }

    private static void TestZeroEntryPipelineSelection(Action<bool, string> check)
    {
        const string rasterSource = """
            struct VsOut { float4 Pos : SV_Position; };
            [shader("vertex")]
            VsOut vertexMain(uint vid : SV_VertexID) { VsOut o; o.Pos = float4(0, 0, 0, 1); return o; }
            [shader("pixel")]
            float4 pixelMain(VsOut i) : SV_Target0 { return float4(1, 0, 0, 1); }
            """;

        using ShaderCompiler compiler = new(ShaderTarget.Spirv);
        CompiledShader graphics = compiler.CompileSource("auto_raster", "auto_raster.slang", rasterSource, ShaderPipelineKind.Graphics);
        check(graphics.Stages.Count == 2 &&
            graphics.FindStage(ShaderStageKind.Vertex) != null &&
            graphics.FindStage(ShaderStageKind.Pixel) != null,
            "CompileSource(..., ShaderPipelineKind.Graphics) auto-selects vertex + pixel without manual entry names");

        const string computeSource = """
            [shader("compute")]
            [numthreads(4, 2, 1)]
            void csMain() {}
            """;
        CompiledShader compute = compiler.CompileSource("auto_compute", "auto_compute.slang", computeSource, ShaderPipelineKind.Compute);
        check(compute.Stages.Count == 1 && compute.GetGpuStage(ShaderStageKind.Compute).ThreadgroupSize.Equals(new Extent3D(4, 2, 1)),
            "CompileSource(..., ShaderPipelineKind.Compute) auto-selects compute stage and threadgroup size");

        bool ambiguousRejected = false;
        try
        {
            const string ambiguousSource = """
                [shader("compute")] [numthreads(1,1,1)] void passA() {}
                [shader("compute")] [numthreads(1,1,1)] void passB() {}
                """;
            compiler.CompileSource("ambiguous_cs", "ambiguous_cs.slang", ambiguousSource, ShaderPipelineKind.Compute);
        }
        catch (ShaderCompilationException)
        {
            ambiguousRejected = true;
        }
        check(ambiguousRejected, "ShaderPipelineKind.Compute rejects ambiguous multi-compute modules with ShaderCompilationException");
    }

    private static void TestAbiValidation(Action<bool, string> check)
    {
        const string badGlobalSource = """
            Texture2D<float4> IllegalGlobalTexture;
            [shader("compute")]
            [numthreads(1, 1, 1)]
            void computeMain()
            {
                float4 _v = IllegalGlobalTexture.Load(int3(0, 0, 0));
            }
            """;

        bool rejected = false;
        string diagnostic = string.Empty;
        using ShaderCompiler validatingCompiler = new(ShaderTarget.Spirv);
        try
        {
            validatingCompiler.CompileSource("bad_abi", "bad_abi.slang", badGlobalSource, ShaderPipelineKind.Compute);
        }
        catch (ShaderCompilationException exception)
        {
            rejected = true;
            diagnostic = exception.Message;
        }
        check(rejected && diagnostic.Contains("ABI", StringComparison.Ordinal),
            "ValidateAbi rejects forbidden global resource bindings outside GPU_ROOT");

        using ShaderCompiler nonValidatingCompiler = new(ShaderTarget.Spirv, new ShaderCompilerOptions { ValidateAbi = false });
        CompiledShader optedOut = nonValidatingCompiler.CompileSource("bad_abi_optout", "bad_abi_optout.slang", badGlobalSource,
            ShaderPipelineKind.Compute);
        check(optedOut.Stages.Count == 1 && optedOut.Reflection.Globals.Count > 0,
            "ValidateAbi = false permits compiling shaders with non-ABI global bindings");
    }

    private static void TestSpecializedRoots(Action<bool, string> check)
    {
        const string namedRootSource = """
            import Core;
            struct Payload { uint First; uint Second; };
            struct GPURootArguments<T> { GPUBufferView Output; T Data; uint Tail; };
            [shader("compute")] [numthreads(1, 1, 1)]
            void computeMain<T>()
            {
                GPURootArguments<T> root = gpu_load_root<GPURootArguments<T>>();
                gpu_store<uint>(root.Output, 0, root.Tail);
            }
            """;
        const string valueRootSource = """
            import Core;
            namespace Pass
            {
                [GPURoot]
                struct ValueRoot<let N : int, let M : int>
                {
                    GPUBufferView Output;
                    uint First[N];
                    uint Second[M];
                    uint Tail;
                };
                [shader("compute")] [numthreads(1, 1, 1)]
                void computeMain<let M : int, let Addend : int, let N : int>()
                {
                    ValueRoot<N, M> root = gpu_load_root<ValueRoot<N, M>>();
                    gpu_store<uint>(root.Output, 0, root.First[0] + root.Second[0] + uint(Addend));
                }
            }
            """;

        using ShaderCompiler compiler = new(ShaderTarget.Spirv);
        CompiledShader typed = compiler.CompileSource("named_generic_root", "named_generic_root.slang", namedRootSource, ShaderPipelineKind.Compute,
            new ShaderCompileOptions { SpecializationArgs = ["Payload"] });
        ShaderRootLayout? typedRoot = typed.Reflection.Root;
        check(typedRoot is { TypeName: "GPURootArguments", SizeBytes: 20, AlignmentBytes: 4 } &&
            typedRoot.Find("Data.Second") is { OffsetBytes: 12, SizeBytes: 4 } && typedRoot.Find("Tail") is { OffsetBytes: 16 },
            "Bare GPURootArguments<T> reflects the concrete specialization rather than its generic declaration");

        CompiledShader valued = compiler.CompileSource("valued_generic_root", "valued_generic_root.slang", valueRootSource, ShaderPipelineKind.Compute,
            new ShaderCompileOptions { SpecializationArgs = ["2", "7", "2 + 3"] });
        ShaderRootLayout? valuedRoot = valued.Reflection.Root;
        check(valuedRoot is { TypeName: "ValueRoot", SizeBytes: 40, AlignmentBytes: 4 } &&
            valuedRoot.Find("First") is { OffsetBytes: 8, SizeBytes: 20, Type.ElementCount: 5 } &&
            valuedRoot.Find("Second") is { OffsetBytes: 28, SizeBytes: 8, Type.ElementCount: 2 } &&
            valuedRoot.Find("Tail") is { OffsetBytes: 36 },
            "A value-only [GPURoot] uses both linked constants matched by name, not unrelated or reordered entry arguments");
    }

    private static void TestSelectedEntryComposition(Action<bool, string> check)
    {
        const string source = """
            import Core;
            interface ISurface { associatedtype Params; float Eval(Params params); };
            struct Parameters { float Gain; };
            struct Surface : ISurface
            {
                typedef Parameters Params;
                float Eval(Parameters params) { return params.Gain; }
            };
            T genericHelper<T>(T value) { return value; }
            interface IUnselected { float Eval(); };
            [shader("compute")] [numthreads(1, 1, 1)] void computeMain<U : IUnselected, V : IUnselected>() {}

            namespace Pass
            {
                [GPURoot]
                struct MixedRoot<let N : int, T : ISurface>
                {
                    T.Params Material;
                    uint Values[N];
                };
                [shader("vertex")]
                float4 vertexMain<T : ISurface, let N : int>(uint vertex : SV_VertexID) : SV_Position
                {
                    MixedRoot<N, T> root = gpu_load_root<MixedRoot<N, T>>();
                    return float4(float(vertex), 0.0, float(root.Values[0]), 1.0);
                }
                [shader("pixel")]
                float4 pixelMain<let N : int, T : ISurface, let Scale : int>() : SV_Target0
                {
                    MixedRoot<N, T> root = gpu_load_root<MixedRoot<N, T>>();
                    T surface;
                    return float4(surface.Eval(root.Material) * float(Scale));
                }
            }
            """;

        using ShaderCompiler compiler = new(ShaderTarget.Spirv);
        ShaderModule template = compiler.LoadModuleFromSource("selected_generic_entries", "selected_generic_entries.slang", source);
        CompiledShader composed = template.CompileComposed(template, ShaderPipelineKind.Graphics,
            new ShaderCompileOptions { SpecializationArgs = ["3", "3", "7"] });
        check(composed.Stages.Count == 2 && composed.FindStage(ShaderStageKind.Vertex) != null && composed.FindStage(ShaderStageKind.Pixel) != null,
            "Composition considers selected namespaced entries, not generic helpers or unselected stages, and inserts type args in each entry's order");
        check(composed.Reflection.Root is { TypeName: "MixedRoot", SizeBytes: 16 } root &&
            root.Find("Material.Gain") is { OffsetBytes: 0, SizeBytes: 4 } && root.Find("Values") is { OffsetBytes: 4, Type.ElementCount: 3 },
            "Composed mixed generic [GPURoot] reflects both the inferred surface type and specialized value");

        (string[] Args, string Diagnostic)[] invalid =
        [
            (["3", "3"], "expects more value specialization arguments"),
            (["3", "3", "7", "8"], "entry points consume 3"),
            (["3", "int", "7"], "specialization failed"),
            (["3", "4", "7"], "specialize root 'MixedRoot' differently"),
        ];
        foreach ((string[] args, string diagnostic) in invalid)
        {
            bool rejected = false;
            try
            {
                template.CompileComposed(template, ShaderPipelineKind.Graphics, new ShaderCompileOptions { SpecializationArgs = args });
            }
            catch (ShaderCompilationException exception)
            {
                rejected = exception.Message.Contains(diagnostic, StringComparison.Ordinal);
            }
            check(rejected, $"Composition rejects invalid specialization: {diagnostic}");
        }
    }

    private static void TestSerializationAndDiskCache(Action<bool, string> check)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "ngs_shader_cache_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            List<string> logs = [];
            ShaderCompilerOptions cacheOptions = new()
            {
                SearchPaths = [],
                CacheDirectory = tempDir,
                Log = message => logs.Add(message),
            };

            CompiledShader first;
            using (ShaderCompiler compiler = new(ShaderTarget.Spirv, cacheOptions))
            {
                first = compiler.CompileSource("cached_test", "cached_test.slang", EmbeddedIncludeSource,
                    ShaderPipelineKind.Compute, new ShaderCompileOptions { SpecializationArgs = ["MatteModel"] });
            }

            string programsDir = Path.Combine(tempDir, "programs");
            string modulesDir = Path.Combine(tempDir, "modules");
            string[] cacheFiles = Directory.Exists(programsDir) ? Directory.GetFiles(programsDir, "*.agshader") : [];
            string[] moduleFiles = Directory.Exists(modulesDir) ? Directory.GetFiles(modulesDir, "*.agmod") : [];
            check(cacheFiles.Length == 1 && moduleFiles.Length >= 1,
                "CacheDirectory persists compiled .agshader and L1 .agmod artifacts to disk");

            byte[] blob = first.Serialize();
            CompiledShader deserialized = CompiledShader.Deserialize(blob);
            check(deserialized.ModuleName == first.ModuleName &&
                deserialized.Target == first.Target &&
                deserialized.Stages.Count == first.Stages.Count &&
                deserialized.Stages[0].Code.Span.SequenceEqual(first.Stages[0].Code.Span) &&
                deserialized.Stages[0].ThreadgroupSize.Equals(first.Stages[0].ThreadgroupSize) &&
                deserialized.Reflection.Root != null &&
                deserialized.Reflection.Root.SizeBytes == first.Reflection.Root!.SizeBytes &&
                deserialized.Reflection.FindType("SampleRoot")?.FindField("Nested.Flags")?.OffsetBytes ==
                    first.Reflection.FindType("SampleRoot")?.FindField("Nested.Flags")?.OffsetBytes &&
                deserialized.Reflection.FindEntryPoint("computeMain")?.GenericParameters.Count == 1,
                "CompiledShader.Serialize / Deserialize preserves bytecode, entry points, and nested reflection");

            bool corruptRejected = false;
            try
            {
                byte[] bad = [1, 2, 3, 4, 5, 6, 7, 8];
                CompiledShader.Deserialize(bad);
            }
            catch (InvalidDataException)
            {
                corruptRejected = true;
            }
            check(corruptRejected, "CompiledShader.Deserialize rejects invalid binary header");

            CompiledShader second;
            using (ShaderCompiler compiler2 = new(ShaderTarget.Spirv, cacheOptions))
            {
                second = compiler2.CompileSource("cached_test", "cached_test.slang", EmbeddedIncludeSource,
                    ShaderPipelineKind.Compute, new ShaderCompileOptions { SpecializationArgs = ["MatteModel"] });
            }
            bool loggedHit = false;
            for (int i = 0; i < logs.Count; i++)
            {
                if (logs[i].Contains("Shader cache hit:", StringComparison.Ordinal))
                    loggedHit = true;
            }
            check(loggedHit && second.Stages[0].Code.Span.SequenceEqual(first.Stages[0].Code.Span),
                "Second CompileSource hits persistent disk cache without recompiling");

            // Modifying specialization arguments or defines must invalidate the L2 cache key while reusing L1 IR.
            CompiledShader third;
            int logCountBeforeInvalidation = logs.Count;
            using (ShaderCompiler compiler3 = new(ShaderTarget.Spirv, cacheOptions))
            {
                third = compiler3.CompileSource("cached_test", "cached_test.slang", EmbeddedIncludeSource,
                    ShaderPipelineKind.Compute, new ShaderCompileOptions { SpecializationArgs = ["GlossModel"] });
            }
            bool hitL2OnChangedSpec = false;
            bool hitL1OnChangedSpec = false;
            for (int i = logCountBeforeInvalidation; i < logs.Count; i++)
            {
                if (logs[i].Contains("Shader cache hit:", StringComparison.Ordinal))
                    hitL2OnChangedSpec = true;
                if (logs[i].Contains("Shader IR cache hit:", StringComparison.Ordinal))
                    hitL1OnChangedSpec = true;
            }
            check(!hitL2OnChangedSpec && hitL1OnChangedSpec && !third.Stages[0].Code.Span.SequenceEqual(first.Stages[0].Code.Span),
                "Changing SpecializationArgs misses disk cache and emits distinct specialized bytecode");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }

        RunAutoInferenceAndCompositionTests(check);
    }

    private enum TestSpecMode : uint
    {
        Fast = 1,
        Accurate = 3,
    }

    private static void RunAutoInferenceAndCompositionTests(Action<bool, string> check)
    {
        // 1. Specialization normalization
        string[] normalized = ShaderCompiler.NormalizeSpecializations([true, false, 42, 7u, TestSpecMode.Accurate, "CustomType"]);
        check(normalized.Length == 6 &&
            normalized[0] == "true" && normalized[1] == "false" &&
            normalized[2] == "42" && normalized[3] == "7" &&
            normalized[4] == "3" && normalized[5] == "CustomType",
            "NormalizeSpecializations formats bool, integer, enum, and string arguments canonically");

        bool rejectedFloat = false;
        try { ShaderCompiler.NormalizeSpecializations([1.5f]); }
        catch (ArgumentException) { rejectedFloat = true; }
        check(rejectedFloat, "NormalizeSpecializations rejects floating-point arguments");

        bool rejectedNull = false;
        try { ShaderCompiler.NormalizeSpecializations([null!]); }
        catch (ArgumentException) { rejectedNull = true; }
        check(rejectedNull, "NormalizeSpecializations rejects null arguments");

        // 2. Auto pipeline kind inference
        const string graphicsSrc = """
            import Core;
            [GPURoot] struct R { float4 Color; };
            struct V { float4 Pos : SV_Position; };
            [shader("vertex")] V vs(uint id : SV_VertexID) { V o; o.Pos = float4(0, 0, 0, 1); return o; }
            [shader("pixel")] float4 ps(V i) : SV_Target0 { return gpu_load_root<R>().Color; }
            """;
        const string computeSrc = """
            import Core;
            [GPURoot] struct R { GPUBufferView Out; };
            [shader("compute")] [numthreads(4, 1, 1)] void cs(uint3 tid : SV_DispatchThreadID) { gpu_store<uint>(gpu_load_root<R>().Out, tid.x, tid.x); }
            """;
        const string ambiguousSrc = """
            import Core;
            [GPURoot] struct R { GPUBufferView Out; };
            [shader("vertex")] float4 vs(uint id : SV_VertexID) : SV_Position { return float4(0, 0, 0, 1); }
            [shader("compute")] [numthreads(1, 1, 1)] void cs(uint3 tid : SV_DispatchThreadID) { gpu_store<uint>(gpu_load_root<R>().Out, 0, 1); }
            """;

        using (ShaderCompiler autoCompiler = new(ShaderTarget.Spirv))
        {
            ShaderModule gfxMod = autoCompiler.LoadModuleFromSource("auto_gfx", "auto_gfx.slang", graphicsSrc);
            check(gfxMod.InferPipelineKind() == ShaderPipelineKind.Graphics && !gfxMod.HasGenericEntryPoints,
                "InferPipelineKind identifies Graphics modules and non-generic entry points");
            CompiledShader gfxCompiled = gfxMod.Compile(ShaderPipelineKind.Auto);
            check(gfxCompiled.Stages.Count == 2 && gfxCompiled.FindStage(ShaderStageKind.Vertex) != null && gfxCompiled.FindStage(ShaderStageKind.Pixel) != null,
                "Compile(ShaderPipelineKind.Auto) compiles Graphics stages automatically");

            ShaderModule csMod = autoCompiler.LoadModuleFromSource("auto_cs", "auto_cs.slang", computeSrc);
            check(csMod.InferPipelineKind() == ShaderPipelineKind.Compute,
                "InferPipelineKind identifies Compute modules");
            CompiledShader csCompiled = csMod.Compile(ShaderPipelineKind.Auto);
            check(csCompiled.Stages.Count == 1 && csCompiled.FindStage(ShaderStageKind.Compute) != null,
                "Compile(ShaderPipelineKind.Auto) compiles Compute stage automatically");

            ShaderModule ambMod = autoCompiler.LoadModuleFromSource("auto_amb", "auto_amb.slang", ambiguousSrc);
            bool rejectedAmbiguous = false;
            try { ambMod.InferPipelineKind(); }
            catch (ShaderCompilationException) { rejectedAmbiguous = true; }
            check(rejectedAmbiguous, "InferPipelineKind rejects modules mixing Graphics and Compute stages");
        }

        // 3. Compositional compilation (Scheme A: [GPURoot] struct PassRoot<T : ISurface> with T.Params material)
        const string contractSrc = """
            module test_contract;
            import Core;
            public interface ITestSurface
            {
                associatedtype Params;
                float4 Eval(uint index, Params params);
            };
            """;
        const string templateSrc = """
            module test_template;
            import Core;
            import test_contract;

            [GPURoot]
            public struct PassRoot<T : ITestSurface>
            {
                public GPUBufferView Output;
                public T.Params Material;
            };

            [shader("compute")]
            [numthreads(1, 1, 1)]
            void computeMain<T : ITestSurface, let scale : int, let invert : bool>(uint3 tid : SV_DispatchThreadID)
            {
                PassRoot<T> root = gpu_load_root<PassRoot<T>>();
                T surface;
                float4 value = surface.Eval(tid.x, root.Material) * float(scale);
                if (invert)
                    value = float4(1.0) - value;
                gpu_store<float4>(root.Output, tid.x, value);
            }
            """;
        const string surfaceASrc = """
            module test_surface_a;
            import Core;
            import test_contract;

            public struct SurfaceAParams
            {
                public GPUTextureId Albedo;
                public GPUSamplerId Sampler;
                public float4 Tint;
            };

            public struct SurfaceA : ITestSurface
            {
                public typedef SurfaceAParams Params;
                public float4 Eval(uint index, SurfaceAParams params)
                {
                    return params.Tint + float4(float(params.Albedo.index), float(params.Sampler.index), 0.0, 0.0) * 0.0;
                }
            };
            """;
        const string surfaceBSrc = """
            module test_surface_b;
            import Core;
            import test_contract;

            public struct SurfaceBParams
            {
                public float Gain;
                public float Bias;
            };

            public struct SurfaceB : ITestSurface
            {
                public typedef SurfaceBParams Params;
                public float4 Eval(uint index, SurfaceBParams params)
                {
                    return float4(params.Gain, params.Bias, 0.0, 1.0);
                }
            };
            """;
        const string surfaceEmptySrc = """
            module test_surface_empty;
            import test_contract;
            public struct Unrelated { public float X; };
            """;
        const string surfaceMultiSrc = """
            module test_surface_multi;
            import test_contract;
            public struct P { public float X; };
            public struct One : ITestSurface { public typedef P Params; public float4 Eval(uint i, P p) { return float4(p.X); } };
            public struct Two : ITestSurface { public typedef P Params; public float4 Eval(uint i, P p) { return float4(p.X * 2); } };
            """;

        Dictionary<string, string> vfs = new(StringComparer.OrdinalIgnoreCase)
        {
            ["test_contract.slang"] = contractSrc,
            ["test_template.slang"] = templateSrc,
            ["test_surface_a.slang"] = surfaceASrc,
            ["test_surface_b.slang"] = surfaceBSrc,
            ["test_surface_empty.slang"] = surfaceEmptySrc,
            ["test_surface_multi.slang"] = surfaceMultiSrc,
        };

        string? ResolveVirtual(string path)
        {
            string fileName = Path.GetFileName(path);
            return vfs.TryGetValue(fileName, out string? src) ? src : null;
        }

        string compCacheDir = Path.Combine(Path.GetTempPath(), "ngs_composed_cache_" + Guid.NewGuid().ToString("N"));
        List<string> compLogs = [];
        try
        {
            ShaderCompilerOptions compOptions = new()
            {
                SearchPaths = ["/virtual"],
                FileResolver = ResolveVirtual,
                PathExists = path => ResolveVirtual(path) != null,
                CacheDirectory = compCacheDir,
                Log = compLogs.Add,
            };

            CompiledShader composedA;
            CompiledShader composedB;
            CompiledShader composedAAlt;
            using (ShaderCompiler compiler = new(ShaderTarget.Spirv, compOptions))
            {
                ShaderModule templateMod = compiler.LoadModule("test_template");
                check(templateMod.HasGenericEntryPoints && templateMod.InferPipelineKind() == ShaderPipelineKind.Compute,
                    "Template module exposes HasGenericEntryPoints and infers Compute pipeline kind before specialization");

                composedA = compiler.CompileComposed("test_template", "test_surface_a", ShaderPipelineKind.Auto,
                    new ShaderCompileOptions { SpecializationArgs = ["2", "false"] });
                composedAAlt = compiler.CompileComposed("test_template", "test_surface_a", ShaderPipelineKind.Auto,
                    new ShaderCompileOptions { SpecializationArgs = ["3", "true"] });
                composedB = compiler.CompileComposed("test_template", "test_surface_b", ShaderPipelineKind.Auto,
                    new ShaderCompileOptions { SpecializationArgs = ["1", "false"] });

                bool rejectedEmpty = false;
                try { compiler.CompileComposed("test_template", "test_surface_empty", ShaderPipelineKind.Auto, new ShaderCompileOptions { SpecializationArgs = ["1", "false"] }); }
                catch (ShaderCompilationException) { rejectedEmpty = true; }
                check(rejectedEmpty, "CompileComposed rejects surface module with zero contract conformers");

                bool rejectedMulti = false;
                try { compiler.CompileComposed("test_template", "test_surface_multi", ShaderPipelineKind.Auto, new ShaderCompileOptions { SpecializationArgs = ["1", "false"] }); }
                catch (ShaderCompilationException) { rejectedMulti = true; }
                check(rejectedMulti, "CompileComposed rejects surface module with multiple contract conformers");
            }

            check(composedA.Reflection.Root != null &&
                composedA.Reflection.Root.Find("Output") is { OffsetBytes: 0, Semantic: ShaderFieldSemantic.BufferView } &&
                composedA.Reflection.Root.Find("Material.Albedo") is { Semantic: ShaderFieldSemantic.TextureId } &&
                composedA.Reflection.Root.Find("Material.Sampler") is { Semantic: ShaderFieldSemantic.SamplerId } &&
                composedA.Reflection.Root.Find("Material.Tint") is { SizeBytes: 16, Semantic: ShaderFieldSemantic.Data },
                "Composed shader reflects generic [GPURoot] PassRoot<SurfaceA> with inline Material.Params nested fields");

            check(composedB.Reflection.Root != null &&
                composedB.Reflection.Root.Find("Material.Gain") is { SizeBytes: 4 } &&
                composedB.Reflection.Root.Find("Material.Bias") is { SizeBytes: 4 } &&
                composedB.Reflection.Root.SizeBytes != composedA.Reflection.Root!.SizeBytes,
                "Composed shader reflects distinct root layout for PassRoot<SurfaceB>");

            check(!composedA.Stages[0].Code.Span.SequenceEqual(composedAAlt.Stages[0].Code.Span),
                "Changing value specialization args on a composed shader emits distinct bytecode");

            // Verify disk cache hit on composed shader
            int logsBefore = compLogs.Count;
            using (ShaderCompiler compiler2 = new(ShaderTarget.Spirv, compOptions))
            {
                CompiledShader cachedA = compiler2.CompileComposed("test_template", "test_surface_a", ShaderPipelineKind.Auto,
                    new ShaderCompileOptions { SpecializationArgs = ["2", "false"] });
                bool hit = false;
                for (int i = logsBefore; i < compLogs.Count; i++)
                {
                    if (compLogs[i].Contains("cache hit", StringComparison.OrdinalIgnoreCase))
                        hit = true;
                }
                check(hit && cachedA.Stages[0].Code.Span.SequenceEqual(composedA.Stages[0].Code.Span),
                    "Second CompileComposed hits persistent disk cache without recompiling");
            }
        }
        finally
        {
            if (Directory.Exists(compCacheDir))
                Directory.Delete(compCacheDir, recursive: true);
        }
    }

    private static void TestL1IrCacheRoundTripAndParity(Action<bool, string> check)
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "ngs_l1_ir_test_" + Guid.NewGuid().ToString("N"));
        string shaderDir = Path.Combine(tempRoot, "shaders");
        string cacheDir = Path.Combine(tempRoot, "cache");
        Directory.CreateDirectory(shaderDir);

        string libBPath = Path.Combine(shaderDir, "ir_lib_b.slang");
        string libAPath = Path.Combine(shaderDir, "ir_lib_a.slang");
        string incPath = Path.Combine(shaderDir, "ir_values.slangh");
        string mainPath = Path.Combine(shaderDir, "ir_main.slang");

        File.WriteAllText(libBPath, """
            module ir_lib_b;
            public float GetBias() { return 0.25; }
            """);
        File.WriteAllText(libAPath, """
            module ir_lib_a;
            import ir_lib_b;
            #include "ir_values.slangh"
            public float ComputeScale(float x) { return x * IR_SCALE + GetBias(); }
            """);
        File.WriteAllText(incPath, "#define IR_SCALE 2.0\n");
        File.WriteAllText(mainPath, """
            module ir_main;
            import Core;
            import ir_lib_a;

            interface ITestOp { float Eval(float v); };
            struct OpAdd : ITestOp { float Eval(float v) { return ComputeScale(v) + 1.0; } };
            struct OpMul : ITestOp { float Eval(float v) { return ComputeScale(v) * 2.0; } };

            [GPURoot]
            struct MainRoot<T : ITestOp>
            {
                GPUBufferView Output;
                float Input;
            };

            [shader("compute")]
            [numthreads(2, 1, 1)]
            void computeMain<T : ITestOp>(uint3 tid : SV_DispatchThreadID)
            {
                MainRoot<T> root = gpu_load_root<MainRoot<T>>();
                T op;
                gpu_store<float>(root.Output, tid.x, op.Eval(root.Input));
            }
            """);

        try
        {
            ShaderCompilerOptions opts = new()
            {
                SearchPaths = [shaderDir],
                CacheDirectory = cacheDir,
            };

            CompiledShader coldCompiled;
            IReadOnlyList<string> coldDeps;
            using (ShaderCompiler compiler1 = new(ShaderTarget.SpirvCompact, opts))
            {
                ShaderModule mod1 = compiler1.LoadModule("ir_main");
                coldDeps = mod1.DependencyPaths;
                check(!mod1.LoadedFromIrCache, "Cold LoadModule does not report L1 IR cache hit");
                coldCompiled = compiler1.Compile("ir_main", ShaderPipelineKind.Compute,
                    new ShaderCompileOptions { SpecializationArgs = ["OpAdd"] });
            }

            string modulesDir = Path.Combine(cacheDir, "modules");
            string programsDir = Path.Combine(cacheDir, "programs");
            check(Directory.GetFiles(modulesDir, "*.agmod").Length >= 3,
                "L1 cache captures target module and transitive imported modules (.agmod)");
            check(Directory.GetFiles(programsDir, "*.agshader").Length == 1,
                "L2 cache writes compiled program artifact (.agshader)");

            // Delete L2 programs directory to force L1 IR restore + re-link/codegen.
            Directory.Delete(programsDir, recursive: true);

            List<string> irLogs = [];
            ShaderCompilerOptions warmIrOpts = new()
            {
                SearchPaths = [shaderDir],
                CacheDirectory = cacheDir,
                Log = irLogs.Add,
            };

            CompiledShader warmCompiled;
            using (ShaderCompiler compiler2 = new(ShaderTarget.SpirvCompact, warmIrOpts))
            {
                ShaderModule restored = compiler2.LoadModule("ir_main");
                IReadOnlyList<string> impls = restored.FindImplementations("ITestOp");
                check(restored.LoadedFromIrCache, "Warm LoadModule restores module from L1 IR blob");
                check(irLogs.Any(l => l.Contains("Shader IR cache hit", StringComparison.Ordinal)),
                    "L1 IR restore emits diagnostic hit log");
                check(impls.Count == 2 && impls.Contains("OpAdd") && impls.Contains("OpMul"),
                    "IR-restored module preserves FindImplementations reflection");
                check(restored.EntryPoints.Count == 1 && restored.EntryPoints[0].HasGenericParameters,
                    "IR-restored module preserves entry-point generic metadata");
                check(restored.DependencyPaths.Count == coldDeps.Count &&
                    restored.DependencyPaths.Any(p => p.EndsWith("ir_lib_b.slang", StringComparison.OrdinalIgnoreCase)) &&
                    restored.DependencyPaths.Any(p => p.EndsWith("ir_values.slangh", StringComparison.OrdinalIgnoreCase)),
                    "IR-restored module preserves full transitive dependency paths");

                warmCompiled = compiler2.Compile("ir_main", ShaderPipelineKind.Compute,
                    new ShaderCompileOptions { SpecializationArgs = ["OpAdd"] });
            }

            check(warmCompiled.Stages[0].Code.Span.SequenceEqual(coldCompiled.Stages[0].Code.Span),
                "L1 IR-restored module produces byte-for-byte identical SPIR-V Compact code");
            check(warmCompiled.Reflection.Root != null &&
                warmCompiled.Reflection.Root.SizeBytes == coldCompiled.Reflection.Root!.SizeBytes &&
                warmCompiled.Reflection.Root.Find("Input")?.OffsetBytes == coldCompiled.Reflection.Root.Find("Input")?.OffsetBytes,
                "L1 IR-restored module produces identical generic [GPURoot] reflection");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static void TestComposedIrAndZeroSessionWarmStart(Action<bool, string> check)
    {
        const string contractSrc = """
            module z_contract;
            import Core;
            public interface IZSurface
            {
                associatedtype Params;
                float4 Eval(Params p);
            };
            """;
        const string templateSrc = """
            module z_template;
            import Core;
            import z_contract;

            [GPURoot]
            public struct PassRoot<T : IZSurface>
            {
                public GPUBufferView Output;
                public T.Params Material;
            };

            [shader("compute")]
            [numthreads(1, 1, 1)]
            void csMain<T : IZSurface, let Gain : int>(uint3 tid : SV_DispatchThreadID)
            {
                PassRoot<T> root = gpu_load_root<PassRoot<T>>();
                T s;
                gpu_store<float4>(root.Output, tid.x, s.Eval(root.Material) * float(Gain));
            }
            """;
        const string surfaceV1 = """
            module z_surface;
            import z_contract;
            public struct ZParams { public float4 Tint; };
            public struct ZSurf : IZSurface
            {
                public typedef ZParams Params;
                public float4 Eval(ZParams p) { return p.Tint; }
            };
            """;
        const string surfaceV2 = """
            module z_surface;
            import z_contract;
            public struct ZParams { public float4 Tint; public float Boost; };
            public struct ZSurf : IZSurface
            {
                public typedef ZParams Params;
                public float4 Eval(ZParams p) { return p.Tint * p.Boost; }
            };
            """;
        const string standaloneSrc = """
            module z_standalone;
            import Core;
            [GPURoot] struct SRoot { float4 Color; };
            [shader("compute")] [numthreads(1, 1, 1)]
            void csMain() { SRoot r = gpu_load_root<SRoot>(); }
            """;
        const string inlineSrc = """
            module z_inline;
            import Core;
            [GPURoot] struct IRoot { float4 Tint; };
            [shader("compute")] [numthreads(1, 1, 1)]
            void csMain() { IRoot r = gpu_load_root<IRoot>(); }
            """;

        string tempRoot = Path.Combine(Path.GetTempPath(), "ngs_zero_session_test_" + Guid.NewGuid().ToString("N"));
        string shaderDir = Path.Combine(tempRoot, "shaders");
        string cacheDir = Path.Combine(tempRoot, "cache");
        Directory.CreateDirectory(shaderDir);
        File.WriteAllText(Path.Combine(shaderDir, "z_contract.slang"), contractSrc);
        File.WriteAllText(Path.Combine(shaderDir, "z_template.slang"), templateSrc);
        File.WriteAllText(Path.Combine(shaderDir, "z_surface.slang"), surfaceV1);
        File.WriteAllText(Path.Combine(shaderDir, "z_standalone.slang"), standaloneSrc);

        try
        {
            byte[] coldComposedCode;
            byte[] coldInlineCode;

            GPUDevice device = Gpu.CreateDevice(new DeviceDesc { Backend = GraphicsBackend.NoGPU });
            try
            {
                using RenderingSystem system = new(device, 1024 * 1024, 1024 * 1024);
                using ShaderSystem shaders = system.CreateShaderSystem(new ShaderCompilerOptions
                {
                    SearchPaths = [shaderDir],
                    CacheDirectory = cacheDir,
                });
                shaders.Compiler.RegisterVirtualModule("z_contract", "z_contract.slang", contractSrc);
                shaders.Compiler.RegisterVirtualModule("z_template", "z_template.slang", templateSrc);
                check(shaders.Compiler.ActiveSessionCount == 0,
                    "RegisterVirtualModule does not eagerly initialize a Slang session");

                Shader standalone = shaders.GetShader("z_standalone", ShaderKind.Compute);
                Shader inlineShader = shaders.GetShaderFromSource("z_inline", "z_inline.slang", inlineSrc, ShaderKind.Compute);
                Shader composed = shaders.Compose("z_template", "z_surface", ShaderKind.Compute);
                Material mat = composed.CreateMaterial("mat", 2);

                coldComposedCode = mat.Variant.Compiled.Stages[0].Code.ToArray();
                coldInlineCode = inlineShader.DefaultVariant.Compiled.Stages[0].Code.ToArray();
                check(standalone.Root.SizeBytes > 0 && mat.Root.Find("Material.Tint") != null,
                    "Cold ShaderSystem compilation produces valid standalone, inline, and composed shaders");
            }
            finally
            {
                device.Destroy();
            }

            // 1. Warm start across ShaderSystem (standalone, GetShaderFromSource, Compose) must create 0 Slang sessions.
            List<string> warmLogs = [];
            GPUDevice device2 = Gpu.CreateDevice(new DeviceDesc { Backend = GraphicsBackend.NoGPU });
            try
            {
                using RenderingSystem system2 = new(device2, 1024 * 1024, 1024 * 1024);
                using ShaderSystem shaders2 = system2.CreateShaderSystem(new ShaderCompilerOptions
                {
                    SearchPaths = [shaderDir],
                    CacheDirectory = cacheDir,
                    Log = warmLogs.Add,
                });
                shaders2.Compiler.RegisterVirtualModule("z_contract", "z_contract.slang", contractSrc);
                shaders2.Compiler.RegisterVirtualModule("z_template", "z_template.slang", templateSrc);

                Shader standalone2 = shaders2.GetShader("z_standalone", ShaderKind.Compute);
                Shader inline2 = shaders2.GetShaderFromSource("z_inline", "z_inline.slang", inlineSrc, ShaderKind.Compute);
                Shader composed2 = shaders2.Compose("z_template", "z_surface", ShaderKind.Compute);
                Material mat2 = composed2.CreateMaterial("mat2", 2);

                check(shaders2.Compiler.ActiveSessionCount == 0,
                    "Warm startup with GetShader, GetShaderFromSource, and Compose creates zero Slang sessions");
                check(inline2.DefaultVariant.Compiled.Stages[0].Code.Span.SequenceEqual(coldInlineCode),
                    "GetShaderFromSource hits L2 disk cache with identical bytecode");
                check(mat2.Variant.Compiled.Stages[0].Code.Span.SequenceEqual(coldComposedCode) &&
                    standalone2.Root.SizeBytes > 0,
                    "Warm composed and standalone shaders match cold compilation");
            }
            finally
            {
                device2.Destroy();
            }

            // 2. Delete L2 programs/ to verify composed L1 IR restore (template + surface both from IR).
            Directory.Delete(Path.Combine(cacheDir, "programs"), recursive: true);
            List<string> irLogs = [];
            using (ShaderCompiler irCompiler = new(ShaderTarget.SpirvCompact, new ShaderCompilerOptions
            {
                SearchPaths = [shaderDir],
                CacheDirectory = cacheDir,
                Log = irLogs.Add,
            }))
            {
                irCompiler.RegisterVirtualModule("z_contract", "z_contract.slang", contractSrc);
                irCompiler.RegisterVirtualModule("z_template", "z_template.slang", templateSrc);
                CompiledShader recomposed = irCompiler.CompileComposed("z_template", "z_surface", ShaderPipelineKind.Compute,
                    new ShaderCompileOptions { SpecializationArgs = ["2"] });
                check(irLogs.Any(l => l.Contains("Shader IR cache hit: 'z_template'", StringComparison.Ordinal)),
                    "Composed recompile after L2 miss restores template from L1 IR cache");
                check(recomposed.Stages[0].Code.Span.SequenceEqual(coldComposedCode),
                    "IR-restored composed shader emits identical bytecode");
            }

            // 3. Edit surface only -> unchanged template still hits L1 IR while surface recompiles with new Boost field.
            File.WriteAllText(Path.Combine(shaderDir, "z_surface.slang"), surfaceV2);
            List<string> partialLogs = [];
            using (ShaderCompiler partialCompiler = new(ShaderTarget.SpirvCompact, new ShaderCompilerOptions
            {
                SearchPaths = [shaderDir],
                CacheDirectory = cacheDir,
                Log = partialLogs.Add,
            }))
            {
                partialCompiler.RegisterVirtualModule("z_contract", "z_contract.slang", contractSrc);
                partialCompiler.RegisterVirtualModule("z_template", "z_template.slang", templateSrc);
                CompiledShader updated = partialCompiler.CompileComposed("z_template", "z_surface", ShaderPipelineKind.Compute,
                    new ShaderCompileOptions { SpecializationArgs = ["2"] });

                check(partialLogs.Any(l => l.Contains("Shader IR cache hit: 'z_template'", StringComparison.Ordinal)),
                    "Editing surface keeps template on L1 IR hit");
                check(!partialLogs.Any(l => l.Contains("Shader IR cache hit: 'z_surface'", StringComparison.Ordinal)),
                    "Editing surface invalidates surface L1 IR and recompiles");
                check(updated.Reflection.Root?.Find("Material.Boost") != null &&
                    !updated.Stages[0].Code.Span.SequenceEqual(coldComposedCode),
                    "Partial recompile reflects edited surface root layout and updated bytecode");
            }
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static void TestMultiTargetCacheIsolation(Action<bool, string> check)
    {
        const string src = """
            module mt_test;
            import Core;
            [GPURoot] struct MtRoot { GPUBufferView Out; float4 Val; };
            [shader("compute")] [numthreads(1, 1, 1)]
            void csMain(uint3 tid : SV_DispatchThreadID)
            {
                MtRoot r = gpu_load_root<MtRoot>();
                gpu_store<float4>(r.Out, tid.x, r.Val);
            }
            """;

        string cacheDir = Path.Combine(Path.GetTempPath(), "ngs_multitarget_cache_" + Guid.NewGuid().ToString("N"));
        List<ShaderTarget> targets = [ShaderTarget.Spirv, ShaderTarget.SpirvCompact, ShaderTarget.MetalSource];
        if (ShaderCompiler.IsTargetSupported(ShaderTarget.Dxil))
            targets.Add(ShaderTarget.Dxil);

        try
        {
            Dictionary<ShaderTarget, byte[]> coldCodes = new();
            foreach (ShaderTarget target in targets)
            {
                using ShaderCompiler compiler = new(target, new ShaderCompilerOptions
                {
                    SearchPaths = [],
                    CacheDirectory = cacheDir,
                });
                CompiledShader compiled = compiler.CompileSource("mt_test", "mt_test.slang", src, ShaderPipelineKind.Compute);
                byte[] code = compiled.Stages[0].Code.ToArray();
                coldCodes[target] = code;

                bool validFormat = target switch
                {
                    ShaderTarget.Spirv or ShaderTarget.SpirvCompact =>
                        code.Length >= 4 && code[0] == 0x03 && code[1] == 0x02 && code[2] == 0x23 && code[3] == 0x07,
                    ShaderTarget.Dxil =>
                        code.Length >= 4 && code[0] == (byte)'D' && code[1] == (byte)'X' && code[2] == (byte)'B' && code[3] == (byte)'C',
                    ShaderTarget.MetalSource =>
                        System.Text.Encoding.UTF8.GetString(code).Contains("kernel", StringComparison.Ordinal),
                    _ => false,
                };
                check(validFormat, $"Multi-target compile emits valid format for {target}");
            }

            check(Directory.GetFiles(Path.Combine(cacheDir, "programs"), "*.agshader").Length == targets.Count,
                "Multi-target L2 cache keeps one .agshader per target without overwriting");
            check(Directory.GetFiles(Path.Combine(cacheDir, "modules"), "*.agmod").Length >= targets.Count,
                "Multi-target L1 cache keeps isolated .agmod files per target");

            // Delete L2 programs and verify L1 IR restores per target without cross-contamination.
            Directory.Delete(Path.Combine(cacheDir, "programs"), recursive: true);
            Dictionary<ShaderTarget, byte[]> recompiledCodes = new();
            foreach (ShaderTarget target in targets)
            {
                List<string> logs = [];
                using ShaderCompiler compiler = new(target, new ShaderCompilerOptions
                {
                    SearchPaths = [],
                    CacheDirectory = cacheDir,
                    Log = logs.Add,
                });
                ShaderModule mod = compiler.LoadModuleFromSource("mt_test", "mt_test.slang", src);
                check(mod.LoadedFromIrCache, $"L1 IR cache restores cleanly for {target}");
                CompiledShader recompiled = compiler.CompileSource("mt_test", "mt_test.slang", src, ShaderPipelineKind.Compute);
                byte[] recompiledCode = recompiled.Stages[0].Code.ToArray();
                recompiledCodes[target] = recompiledCode;
                bool irMatches = target == ShaderTarget.MetalSource
                    ? NormalizeMslForIrParity(recompiledCode) == NormalizeMslForIrParity(coldCodes[target])
                    : recompiledCode.AsSpan().SequenceEqual(coldCodes[target]);
                check(irMatches, $"L1 IR compile reproduces exact bytecode for {target}");
            }

            // Now verify L2 hit with 0 Slang sessions across all targets sharing cacheDir.
            foreach (ShaderTarget target in targets)
            {
                using ShaderCompiler compiler = new(target, new ShaderCompilerOptions
                {
                    SearchPaths = [],
                    CacheDirectory = cacheDir,
                });
                CompiledShader hit = compiler.CompileSource("mt_test", "mt_test.slang", src, ShaderPipelineKind.Compute);
                check(compiler.ActiveSessionCount == 0 && hit.Stages[0].Code.Span.SequenceEqual(recompiledCodes[target]),
                    $"Shared cache hits L2 with zero Slang sessions for {target}");
            }
        }
        finally
        {
            if (Directory.Exists(cacheDir))
                Directory.Delete(cacheDir, recursive: true);
        }
    }

    private static void TestCacheStalenessAndCorruptionSafety(Action<bool, string> check)
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "ngs_corrupt_stale_test_" + Guid.NewGuid().ToString("N"));
        string shaderDir = Path.Combine(tempRoot, "shaders");
        string cacheDir = Path.Combine(tempRoot, "cache");
        Directory.CreateDirectory(shaderDir);

        string incPath = Path.Combine(shaderDir, "sc_vals.slangh");
        string libPath = Path.Combine(shaderDir, "sc_lib.slang");
        string mainPath = Path.Combine(shaderDir, "sc_main.slang");

        File.WriteAllText(incPath, "#define SC_ADD 1.0\n");
        File.WriteAllText(libPath, """
            module sc_lib;
            #include "sc_vals.slangh"
            public float GetVal() { return 2.0 + SC_ADD; }
            """);
        File.WriteAllText(mainPath, """
            module sc_main;
            import Core;
            import sc_lib;
            [GPURoot] struct ScRoot { GPUBufferView Out; };
            [shader("compute")] [numthreads(1, 1, 1)]
            void csMain() { ScRoot r = gpu_load_root<ScRoot>(); gpu_store<float>(r.Out, 0, GetVal()); }
            """);

        try
        {
            ShaderCompilerOptions opts = new() { SearchPaths = [shaderDir], CacheDirectory = cacheDir };
            byte[] baselineCode;
            using (ShaderCompiler c1 = new(ShaderTarget.SpirvCompact, opts))
                baselineCode = c1.Compile("sc_main", ShaderPipelineKind.Compute).Stages[0].Code.ToArray();

            // 1. Corrupt L1 .agmod IR payload and L2 .agshader payload; must not crash Slang and must self-heal.
            foreach (string modFile in Directory.GetFiles(Path.Combine(cacheDir, "modules"), "*.agmod"))
            {
                byte[] bytes = File.ReadAllBytes(modFile);
                if (bytes.Length > 64)
                {
                    bytes[^5] ^= 0xFF;
                    File.WriteAllBytes(modFile, bytes);
                }
            }
            foreach (string progFile in Directory.GetFiles(Path.Combine(cacheDir, "programs"), "*.agshader"))
            {
                byte[] bytes = File.ReadAllBytes(progFile);
                if (bytes.Length > 48)
                {
                    bytes[^3] ^= 0xFF;
                    File.WriteAllBytes(progFile, bytes);
                }
            }

            List<string> repairLogs = [];
            using (ShaderCompiler c2 = new(ShaderTarget.SpirvCompact, new ShaderCompilerOptions
            {
                SearchPaths = [shaderDir],
                CacheDirectory = cacheDir,
                Log = repairLogs.Add,
            }))
            {
                ShaderModule mod = c2.LoadModule("sc_main");
                check(!mod.LoadedFromIrCache, "Corrupt .agmod IR checksum rejects blob safely without crashing Slang");
                CompiledShader repaired = c2.Compile("sc_main", ShaderPipelineKind.Compute);
                check(repaired.Stages[0].Code.Span.SequenceEqual(baselineCode),
                    "Corrupt L1/L2 cache falls back to clean recompilation and matches baseline");
            }

            // Verify self-healed L2 now hits cleanly.
            using (ShaderCompiler c3 = new(ShaderTarget.SpirvCompact, opts))
            {
                CompiledShader healed = c3.Compile("sc_main", ShaderPipelineKind.Compute);
                check(c3.ActiveSessionCount == 0 && healed.Stages[0].Code.Span.SequenceEqual(baselineCode),
                    "Repaired L2 cache hits cleanly on subsequent run");
            }

            // 2. Edit transitive #include header -> both L1 and L2 must miss and produce updated code.
            File.WriteAllText(incPath, "#define SC_ADD 9.0\n");
            using (ShaderCompiler c4 = new(ShaderTarget.SpirvCompact, opts))
            {
                CompiledShader updatedFromInclude = c4.Compile("sc_main", ShaderPipelineKind.Compute);
                check(c4.ActiveSessionCount == 1 && !updatedFromInclude.Stages[0].Code.Span.SequenceEqual(baselineCode),
                    "Modifying transitive #include invalidates L1 and L2 caches");
            }

            // 3. Edit imported module -> both L1 and L2 must miss and produce updated code.
            File.WriteAllText(libPath, """
                module sc_lib;
                #include "sc_vals.slangh"
                public float GetVal() { return 5.0 + SC_ADD; }
                """);
            using (ShaderCompiler c5 = new(ShaderTarget.SpirvCompact, opts))
            {
                CompiledShader updatedFromLib = c5.Compile("sc_main", ShaderPipelineKind.Compute);
                check(c5.ActiveSessionCount == 1 && !updatedFromLib.Stages[0].Code.Span.SequenceEqual(baselineCode),
                    "Modifying imported library invalidates L1 and L2 caches");
            }
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static string NormalizeMslForIrParity(byte[] mslBytes)
    {
        string text = System.Text.Encoding.UTF8.GetString(mslBytes);
        string[] rawLines = text.Split('\n');
        List<string> normalized = new(rawLines.Length);
        for (int i = 0; i < rawLines.Length; i++)
        {
            string line = rawLines[i].TrimEnd('\r');
            string trimmed = line.TrimStart();
            if (trimmed.Length == 0 || trimmed.StartsWith("#line", StringComparison.Ordinal))
                continue;
            normalized.Add(line);
        }
        return string.Join('\n', normalized);
    }
}
