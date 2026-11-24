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

            string[] cacheFiles = Directory.Exists(tempDir) ? Directory.GetFiles(tempDir, "*.agshader") : [];
            check(cacheFiles.Length == 1, "CacheDirectory persists compiled .agshader artifact to disk");

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
                if (logs[i].Contains("cache hit", StringComparison.OrdinalIgnoreCase))
                    loggedHit = true;
            }
            check(loggedHit && second.Stages[0].Code.Span.SequenceEqual(first.Stages[0].Code.Span),
                "Second CompileSource hits persistent disk cache without recompiling");

            // Modifying specialization arguments or defines must invalidate the cache key cleanly.
            CompiledShader third;
            int logCountBeforeInvalidation = logs.Count;
            using (ShaderCompiler compiler3 = new(ShaderTarget.Spirv, cacheOptions))
            {
                third = compiler3.CompileSource("cached_test", "cached_test.slang", EmbeddedIncludeSource,
                    ShaderPipelineKind.Compute, new ShaderCompileOptions { SpecializationArgs = ["GlossModel"] });
            }
            bool hitOnChangedSpec = false;
            for (int i = logCountBeforeInvalidation; i < logs.Count; i++)
            {
                if (logs[i].Contains("cache hit", StringComparison.OrdinalIgnoreCase))
                    hitOnChangedSpec = true;
            }
            check(!hitOnChangedSpec && !third.Stages[0].Code.Span.SequenceEqual(first.Stages[0].Code.Span),
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
}
