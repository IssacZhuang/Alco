using NUnit.Framework;

namespace Alco.ShaderCompiler;

// ─────────────────────────────────────────────────────────────────────────────
// Phase-1 unit tests for the ShaderSystem's headless core (plan §4.2): module
// cache, dependency graph, reverse invalidation and the two disk-cache layers
// (module IR + linked programs) — hit/miss/invalidation/staleness.
// ─────────────────────────────────────────────────────────────────────────────
[TestFixture]
public class SlangModuleSystemTest
{
    private const string MainModule = """
        import alco_sys_lib;

        cbuffer _frame : register(b0, space0)
        {
            float4x4 viewProjection;
        };

        struct VSInput
        {
            float3 position : POSITION;
        };

        struct VSOutput
        {
            float4 position : SV_POSITION;
        };

        [shader("vertex")]
        VSOutput MainVS(VSInput input)
        {
            VSOutput output;
            output.position = mul(viewProjection, float4(input.position, 1.0));
            return output;
        }

        [shader("fragment")]
        float4 MainPS() : SV_TARGET
        {
            return LibColor();
        }
        """;

    private const string LibModule = """
        export float4 LibColor()
        {
            return float4(1, 0, 0, 1);
        }
        """;

    // A file module with its own `module` declaration and a define-selected body.
    private const string DefinePermutedModule = """
        module define_permuted;

        cbuffer _output : register(b0, space0)
        {
            RWStructuredBuffer<float4> _output;
        };

        [shader("compute")]
        [numthreads(1, 1, 1)]
        void MainCS(uint3 id : SV_DispatchThreadID)
        {
        #ifdef NOISE_DETAIL
            _output[id.x] = float4(1);
        #else
            _output[id.x] = float4(0);
        #endif
        }
        """;

    [Test]
    public void DefinePermutations_OfOneModule_CoexistInOneSession()
    {
        Dictionary<string, string> files = new() { ["define_permuted.slang"] = DefinePermutedModule };
        using SlangModuleSystem system = new(OptionsFor(files), null);

        // Both permutations share the file's source, including its `module X;`
        // declaration. slang keys a session's module table by the DECLARED name,
        // so the permutation must re-declare a mangled one — a second load under
        // the original declaration trips slang's dictionary assert.
        system.GetOrLoadModule("define_permuted");
        Assert.DoesNotThrow(() => system.GetOrLoadModule("define_permuted", ["NOISE_DETAIL"]));

        using SlangProgram plain = system.GetProgramAllEntries("define_permuted", []);
        using SlangProgram detailed = system.GetProgramAllEntries("define_permuted", [], ["NOISE_DETAIL"]);

        Assert.Multiple(() =>
        {
            Assert.That(plain.EntryCode[0].Length, Is.GreaterThan(4));
            Assert.That(detailed.EntryCode[0].Length, Is.GreaterThan(4));
            Assert.That(system.GetLoadedModuleNames(), Is.EquivalentTo(new[] { "define_permuted" }));
        });
    }

    private static SlangCompilerOptions OptionsFor(Dictionary<string, string> files) => new()
    {
        // Beachhead-style resolution: exact virtual path, then filename lookup
        // across the tree (slang combines import paths relative to the importer).
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

    private static SlangCompilerOptions OptionsFor(Dictionary<string, string> files, SlangCodeTarget target)
    {
        SlangCompilerOptions options = OptionsFor(files);
        return new SlangCompilerOptions
        {
            SearchPaths = options.SearchPaths,
            PreprocessorMacros = options.PreprocessorMacros,
            Resolver = options.Resolver,
            Exists = options.Exists,
            Target = target,
        };
    }

    private static Dictionary<string, string> DefaultFiles() => new()
    {
        ["shaders/alco_sys_main.slang"] = MainModule,
        ["alco_sys_lib.slang"] = LibModule,
    };

    private static string TempCache()
        => Path.Combine(Path.GetTempPath(), $"alco_slang_sys_{Guid.NewGuid():N}");

    private static List<SlangEntryPointRequest> GraphicsEntries() =>
    [
        new SlangEntryPointRequest("MainVS", Alco.Graphics.ShaderStage.Vertex),
        new SlangEntryPointRequest("MainPS", Alco.Graphics.ShaderStage.Fragment),
    ];

    [Test]
    public void ModuleCache_ReusesLoadedModule()
    {
        Dictionary<string, string> files = DefaultFiles();
        using SlangModuleSystem system = new(OptionsFor(files), null);

        SlangModuleHandle first = system.GetOrLoadModule("alco_sys_main", "shaders/alco_sys_main.slang", MainModule);
        SlangModuleHandle second = system.GetOrLoadModule("alco_sys_main", "shaders/alco_sys_main.slang", MainModule);
        using SlangProgram program = system.GetProgram("alco_sys_main", GraphicsEntries(), []);

        Assert.Multiple(() =>
        {
            Assert.That(second, Is.SameAs(first), "same module must be returned within one system");
            Assert.That(program.EntryCode, Has.Length.EqualTo(2));
            Assert.That(program.EntryCode[0].Length, Is.GreaterThan(4));
            // The imported lib is part of the dependency graph (transitive).
            Assert.That(system.GetModuleDependencies("alco_sys_main"), Has.Some.Contains("alco_sys_lib.slang"));
        });
    }

    [Test]
    public void ModuleIRDiskCache_RestoresAcrossSystems()
    {
        Dictionary<string, string> files = DefaultFiles();
        string cache = TempCache();
        try
        {
            byte[][] firstCode;
            using (SlangModuleSystem system = new(OptionsFor(files), cache))
            {
                system.GetOrLoadModule("alco_sys_main", "shaders/alco_sys_main.slang", MainModule);
                using SlangProgram program = system.GetProgram("alco_sys_main", GraphicsEntries(), []);
                firstCode = program.EntryCode;
            }

            using SlangModuleSystem restored = new(OptionsFor(files), cache);
            restored.GetOrLoadModule("alco_sys_main", "shaders/alco_sys_main.slang", MainModule);
            using SlangProgram cached = restored.GetProgram("alco_sys_main", GraphicsEntries(), []);

            Assert.Multiple(() =>
            {
                Assert.That(restored.IsModuleLoadedFromCache("alco_sys_main"), Is.True,
                    "module must be restored from the .slang-module IR cache");
                Assert.That(cached.EntryCode[0], Is.EqualTo(firstCode[0]).AsCollection,
                    "restored vertex SPIR-V differs");
                Assert.That(cached.EntryCode[1], Is.EqualTo(firstCode[1]).AsCollection,
                    "restored fragment SPIR-V differs");
                Assert.That(cached.GetUniformMembers("_frame").Count, Is.GreaterThan(0),
                    "uniform members must survive the program cache round-trip");
                Assert.That(Directory.GetFiles(Path.Combine(cache, "modules"), "*.slang-module"), Has.Length.EqualTo(1));
                Assert.That(Directory.GetFiles(Path.Combine(cache, "programs"), "*.bin"), Has.Length.EqualTo(1));
            });
        }
        finally
        {
            Directory.Delete(cache, true);
        }
    }

    [Test]
    public void ModuleIRDiskCache_NameKeyedModules_RestoreAcrossSystems()
    {
        // Name-keyed loads (the engine's GetShader route) probe the module by
        // name→file conventions; the probe candidate must become the module's
        // path identity. The extension-less module name previously used as the
        // identity resolved to nothing through the resolver, so cache writes
        // succeeded while every read missed — each run re-parsed the module.
        Dictionary<string, string> files = new()
        {
            ["shaders/name-keyed.slang"] = MainModule,
            ["alco_sys_lib.slang"] = LibModule,
        };
        string cache = TempCache();
        try
        {
            using (SlangModuleSystem system = new(OptionsFor(files), cache))
            {
                system.GetOrLoadModule("name-keyed");
                using SlangProgram program = system.GetProgramAllEntries("name-keyed", []);
                Assert.That(program.EntryCode[0].Length, Is.GreaterThan(4));
            }

            using SlangModuleSystem restored = new(OptionsFor(files), cache);
            restored.GetOrLoadModule("name-keyed");
            using SlangProgram cached = restored.GetProgramAllEntries("name-keyed", []);

            Assert.Multiple(() =>
            {
                Assert.That(restored.IsModuleLoadedFromCache("name-keyed"), Is.True,
                    "the name-keyed module must restore from the IR cache");
                Assert.That(cached.EntryCode[0].Length, Is.GreaterThan(4));
            });
        }
        finally
        {
            Directory.Delete(cache, true);
        }
    }

    [Test]
    public void ModuleIRDiskCache_DefinePermutations_RestoreAcrossSystems()
    {
        // A permutation's own path identity is a disambiguated name no resolver
        // can address; its staleness must come from the hashed permutation
        // source instead of resolver lookups of the fabricated path.
        Dictionary<string, string> files = new() { ["shaders/define_permuted.slang"] = DefinePermutedModule };
        string cache = TempCache();
        try
        {
            using (SlangModuleSystem system = new(OptionsFor(files), cache))
            {
                system.GetOrLoadModule("define_permuted");
                system.GetOrLoadModule("define_permuted", ["NOISE_DETAIL"]);
                using SlangProgram plain = system.GetProgramAllEntries("define_permuted", []);
                using SlangProgram detailed = system.GetProgramAllEntries("define_permuted", [], ["NOISE_DETAIL"]);
                Assert.That(plain.EntryCode[0].Length, Is.GreaterThan(4));
                Assert.That(detailed.EntryCode[0].Length, Is.GreaterThan(4));
            }

            using SlangModuleSystem restored = new(OptionsFor(files), cache);
            restored.GetOrLoadModule("define_permuted");
            restored.GetOrLoadModule("define_permuted", ["NOISE_DETAIL"]);

            Assert.Multiple(() =>
            {
                Assert.That(restored.IsModuleLoadedFromCache("define_permuted"), Is.True,
                    "the base module must restore from the IR cache");
                Assert.That(restored.IsModuleLoadedFromCache("define_permuted|NOISE_DETAIL"), Is.True,
                    "the define permutation must restore from the IR cache");
            });
        }
        finally
        {
            Directory.Delete(cache, true);
        }
    }

    [Test]
    public void ModuleIRDiskCache_StaleWhenSourceChanges()
    {
        Dictionary<string, string> files = DefaultFiles();
        string cache = TempCache();
        try
        {
            using (SlangModuleSystem system = new(OptionsFor(files), cache))
            {
                system.GetOrLoadModule("alco_sys_main", "shaders/alco_sys_main.slang", MainModule);
                using SlangProgram program = system.GetProgram("alco_sys_main", GraphicsEntries(), []);
                Assert.That(program.EntryCode[1].Length, Is.GreaterThan(4));
            }

            // Edit the imported lib: its recorded hash no longer matches.
            files["alco_sys_lib.slang"] = LibModule.Replace("float4(1, 0, 0, 1)", "float4(0, 1, 0, 1)");

            using SlangModuleSystem system2 = new(OptionsFor(files), cache);
            system2.GetOrLoadModule("alco_sys_main", "shaders/alco_sys_main.slang", MainModule);
            Assert.That(system2.IsModuleLoadedFromCache("alco_sys_main"), Is.False,
                "a changed dependency must invalidate the IR cache entry");
        }
        finally
        {
            Directory.Delete(cache, true);
        }
    }

    [Test]
    public void Invalidate_ModulesContaining_InvalidatesImportersAndFiresEvent()
    {
        Dictionary<string, string> files = DefaultFiles();
        using SlangModuleSystem system = new(OptionsFor(files), null);
        system.GetOrLoadModule("alco_sys_main", "shaders/alco_sys_main.slang", MainModule);
        using SlangProgram before = system.GetProgram("alco_sys_main", GraphicsEntries(), []);

        List<IReadOnlyList<string>> events = [];
        system.ModulesInvalidated += events.Add;

        // Changing an unrelated file is a no-op.
        Assert.That(system.InvalidateModulesContaining("shaders/unrelated.slang"), Is.Empty);
        Assert.That(events, Is.Empty);

        // Changing the lib invalidates its importer. Watcher paths must be in the
        // dependency graph's (recorded) path space — the engine resolver keeps
        // these consistent.
        string libPath = system.GetModuleDependencies("alco_sys_main")
            .First(dep => dep.Contains("alco_sys_lib"));
        IReadOnlyList<string> affected = system.InvalidateModulesContaining(libPath);
        Assert.Multiple(() =>
        {
            Assert.That(affected, Is.EqualTo(new[] { "alco_sys_main" }));
            Assert.That(events, Has.Count.EqualTo(1));
            Assert.That(events[0], Is.EqualTo(new[] { "alco_sys_main" }));
            Assert.That(system.GetModuleDependencies("alco_sys_main"), Is.Empty,
                "module cache must be dropped after invalidation");
        });

        // The program was disposed by the rebuild; a new one compiles from the
        // (changed) source through the fresh session.
        files["alco_sys_lib.slang"] = LibModule.Replace("float4(1, 0, 0, 1)", "float4(0, 0, 1, 1)");
        system.GetOrLoadModule("alco_sys_main", "shaders/alco_sys_main.slang", MainModule);
        using SlangProgram after = system.GetProgram("alco_sys_main", GraphicsEntries(), []);
        Assert.That(after.EntryCode[1].Length, Is.GreaterThan(4));
    }

    [Test]
    public void ProgramAndModuleCaches_AreKeyedByCodeTarget()
    {
        // One cache directory serves a machine that switches graphics backends
        // (Vulkan ↔ D3D12): a module/program compiled for one code target must
        // never be restored by a session emitting another.
        Dictionary<string, string> files = DefaultFiles();
        string cache = TempCache();
        try
        {
            byte[][] spirvCode;
            using (SlangModuleSystem spirvSystem = new(OptionsFor(files, SlangCodeTarget.Spirv), cache))
            {
                spirvSystem.GetOrLoadModule("alco_sys_main", "shaders/alco_sys_main.slang", MainModule);
                using SlangProgram program = spirvSystem.GetProgram("alco_sys_main", GraphicsEntries(), []);
                spirvCode = program.EntryCode;
            }

            using SlangModuleSystem dxilSystem = new(OptionsFor(files, SlangCodeTarget.Dxil), cache);
            dxilSystem.GetOrLoadModule("alco_sys_main", "shaders/alco_sys_main.slang", MainModule);
            using SlangProgram dxilProgram = dxilSystem.GetProgram("alco_sys_main", GraphicsEntries(), []);

            Assert.Multiple(() =>
            {
                Assert.That(dxilSystem.IsModuleLoadedFromCache("alco_sys_main"), Is.False,
                    "a DXIL session must not restore a module IR stamped for SPIR-V");
                Assert.That(dxilProgram.EntryCode[0][0..4],
                    Is.EqualTo(new byte[] { (byte)'D', (byte)'X', (byte)'B', (byte)'C' }),
                    "DXIL session must emit DXBC containers");
                Assert.That(spirvCode[0][0..4],
                    Is.EqualTo(new byte[] { 0x03, 0x02, 0x23, 0x07 }),
                    "the SPIR-V session's bytecode is untouched");
                Assert.That(Directory.GetFiles(Path.Combine(cache, "modules"), "*.slang-module"),
                    Has.Length.EqualTo(2), "module IR entries per target");
                Assert.That(Directory.GetFiles(Path.Combine(cache, "programs"), "*.bin"),
                    Has.Length.EqualTo(2), "program entries per target");
            });
        }
        finally
        {
            Directory.Delete(cache, true);
        }
    }

    [Test]
    public void GetProgram_DistinctSpecializationsAreDistinctPrograms()
    {
        // Interface specialization — the mechanism the material system uses (D3).
        const string genericModule = """
            public interface IColor
            {
                float4 Get();
            }

            public struct Red : IColor
            {
                public float4 Get() { return float4(1, 0, 0, 1); }
            }

            public struct Green : IColor
            {
                public float4 Get() { return float4(0, 1, 0, 1); }
            }

            [shader("fragment")]
            float4 MainPS<T : IColor>() : SV_TARGET
            {
                T color;
                return color.Get();
            }
            """;
        Dictionary<string, string> files = new() { ["shaders/generic.slang"] = genericModule };
        using SlangModuleSystem system = new(OptionsFor(files), null);
        system.GetOrLoadModule("generic", "shaders/generic.slang", genericModule);

        List<SlangEntryPointRequest> entries = [new("MainPS", Alco.Graphics.ShaderStage.Fragment)];
        using SlangProgram red = system.GetProgram("generic", entries, ["Red"]);
        using SlangProgram green = system.GetProgram("generic", entries, ["Green"]);
        using SlangProgram redAgain = system.GetProgram("generic", entries, ["Red"]);

        Assert.Multiple(() =>
        {
            Assert.That(redAgain, Is.SameAs(red), "same specialization must return the cached program");
            Assert.That(green, Is.Not.SameAs(red), "different specialization must compile separately");
            Assert.That(red.EntryCode[0].Length, Is.GreaterThan(4));
            Assert.That(green.EntryCode[0].Length, Is.GreaterThan(4));
        });
    }

    [Test]
    public void GetProgram_IntValueSpecializationsAreDistinctPrograms()
    {
        // Generic value parameters (plan D3): integer-literal arguments drive the
        // engine's variant axes (fxaa quality, sRGB compression, cloud noise bake).
        const string valueGenericModule = """
            [shader("fragment")]
            float4 MainPS<let Channel : int>() : SV_TARGET
            {
                if (Channel == 0) { return float4(1, 0, 0, 1); }
                return float4(0, 1, 0, 1);
            }
            """;
        Dictionary<string, string> files = new() { ["shaders/value-generic.slang"] = valueGenericModule };
        using SlangModuleSystem system = new(OptionsFor(files), null);
        system.GetOrLoadModule("value_generic", "shaders/value-generic.slang", valueGenericModule);

        List<SlangEntryPointRequest> entries = [new("MainPS", Alco.Graphics.ShaderStage.Fragment)];
        using SlangProgram zero = system.GetProgram("value_generic", entries, ["0"]);
        using SlangProgram one = system.GetProgram("value_generic", entries, ["1"]);
        using SlangProgram zeroAgain = system.GetProgram("value_generic", entries, ["0"]);

        Assert.Multiple(() =>
        {
            Assert.That(zeroAgain, Is.SameAs(zero), "same value specialization must return the cached program");
            Assert.That(one, Is.Not.SameAs(zero), "different value specialization must compile separately");
            Assert.That(zero.EntryCode[0].Length, Is.GreaterThan(4));
            Assert.That(one.EntryCode[0].Length, Is.GreaterThan(4));
            Assert.That(one.EntryCode[0], Is.Not.EqualTo(zero.EntryCode[0]),
                "distinct values must produce distinct target code");
        });
    }
}
