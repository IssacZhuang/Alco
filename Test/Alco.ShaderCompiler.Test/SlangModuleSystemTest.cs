using NUnit.Framework;

namespace Alco.ShaderCompiler;

// ─────────────────────────────────────────────────────────────────────────────
// Unit tests for the ShaderSystem's headless core: module cache, dependency
// graph, reverse invalidation and the two disk-cache layers (module IR +
// linked programs) — hit/miss/invalidation/staleness.
// ─────────────────────────────────────────────────────────────────────────────
[TestFixture]
public class SlangModuleSystemTest
{
    private const string MainModule = """
        import alco_sys_lib;

        cbuffer frame : register(b0, space0)
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
                Assert.That(cached.GetUniformMembers("frame").Count, Is.GreaterThan(0),
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
        // name→file conventions. The probe candidate must become the module's
        // path identity — an unresolvable identity silently turns every cache
        // read into a re-parse.
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
        DxilDownstreamAvailability.AssertAvailable();
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
        // Generic value parameters: integer-literal arguments drive the
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

    [Test]
    public void GetProgram_BoolValueSpecializationsAcceptSlangLiteralsOnly()
    {
        // Feasibility probe for object-typed specialization arguments. The string
        // is handed to slang as an *expression* (SlangSpecializationArg.FromExpr),
        // so only slang spellings are valid: bool axes take the lowercase literals
        // "true"/"false" — C# bool.ToString() yields "True"/"False", which the
        // expression parser must reject (no such identifier in scope).
        const string boolGenericModule = """
            [shader("fragment")]
            float4 MainPS<let Flag : bool>() : SV_TARGET
            {
                if (Flag) { return float4(1, 0, 0, 1); }
                return float4(0, 1, 0, 1);
            }
            """;
        Dictionary<string, string> files = new() { ["shaders/bool-generic.slang"] = boolGenericModule };
        using SlangModuleSystem system = new(OptionsFor(files), null);
        system.GetOrLoadModule("bool_generic", "shaders/bool-generic.slang", boolGenericModule);

        List<SlangEntryPointRequest> entries = [new("MainPS", Alco.Graphics.ShaderStage.Fragment)];
        using SlangProgram lowered = system.GetProgram("bool_generic", entries, ["true"]);
        using SlangProgram loweredAgain = system.GetProgram("bool_generic", entries, ["true"]);
        using SlangProgram upper = system.GetProgram("bool_generic", entries, ["false"]);

        Assert.Multiple(() =>
        {
            Assert.That(loweredAgain, Is.SameAs(lowered), "same bool specialization must return the cached program");
            Assert.That(upper.EntryCode[0], Is.Not.EqualTo(lowered.EntryCode[0]),
                "distinct bool values must produce distinct target code");
        });

        Assert.That(() => system.GetProgram("bool_generic", entries, ["True"]),
            Throws.Exception,
                "the C# bool.ToString() spelling must be rejected — object arguments need normalization to slang literals");
    }

    [Test]
    public void GetProgram_ValueAxisTypeMatrix()
    {
        // slang generic value parameters accept integer and enum types only
        // (E30624) — float/double/half axes are rejected at module parse time,
        // so object-argument normalization only needs to cover bool, integer
        // forms (C# ToString is invariant digits, no suffix) and identifier
        // passthrough. Locked here so the normalization contract follows
        // slang's actual surface, not assumptions.
        (string Type, string Form, bool Supported)[] cases =
        [
            ("int", "1", true),
            ("uint", "1", true),
            ("uint", "1u", true),
            ("bool", "true", true),
            ("float", "1.5", false),
            ("double", "1.5", false),
            ("half", "0.5", false),
        ];
        foreach ((string type, string form, bool supported) in cases)
        {
            string src = $$"""
                [shader("fragment")]
                float4 MainPS<let X : {{type}}>() : SV_TARGET
                {
                    return float4(float(X), 0, 0, 1);
                }
                """;
            Dictionary<string, string> files = new() { ["shaders/axis.slang"] = src };
            using SlangModuleSystem system = new(OptionsFor(files), null);

            if (supported)
            {
                system.GetOrLoadModule("axis", "shaders/axis.slang", src);
                Assert.That(() => system.GetProgram(
                    "axis", [new("MainPS", Alco.Graphics.ShaderStage.Fragment)], [form]),
                    Throws.Nothing, $"{type} axis with '{form}' must specialize");
            }
            else
            {
                Assert.That(() => system.GetOrLoadModule("axis", "shaders/axis.slang", src),
                    Throws.Exception, $"{type} axes are not supported by slang (E30624)");
            }
        }
    }
}
