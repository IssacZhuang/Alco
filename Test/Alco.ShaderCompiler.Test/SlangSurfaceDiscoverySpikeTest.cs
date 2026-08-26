using Alco.Graphics;
using System.Numerics;
using NUnit.Framework;

namespace Alco.ShaderCompiler;

// ─────────────────────────────────────────────────────────────────────────────
// Spike: can slang's own reflection discover the surface type, so the engine
// never passes a type name ("Surface" by convention) that a shader author may
// have named differently? The candidate mechanisms, all from slang's public
// reflection API (exported by the vendored slang.dll):
//
//   1. IModule::getModuleReflection() → DeclReflection* — a module's own
//      declaration tree: child decls by kind (Struct/Func/Generic/...), each
//      convertible to its TypeReflection (spReflection_getTypeFromDecl).
//   2. spReflection_isSubType — does a struct implement the contract
//      interface? Discovery keyed by the contract itself, not by any name of
//      the implementation.
//   3. The contract interface type pulled from the TEMPLATE side — the
//      entry point's generic constraint (`MainPS<T : ISurface>`) via generic
//      reflection — so even "ISurface" is not an engine-side convention.
//   4. Specialization by Kind=Type (a TypeReflection*) instead of Kind=Expr
//      (the "Surface" string) — the discovered type goes straight into
//      Specialize, mixed freely with value (Expr) arguments.
//   5. spReflection_specializeType — generic application at the type level
//      (Stack<Snow, Base>): multi-library composition without generating a
//      wrapper module.
// ─────────────────────────────────────────────────────────────────────────────
[TestFixture]
public unsafe class SlangSurfaceDiscoverySpikeTest
{
    private const string Contract = """
        #language slang 2025
        module disc_contract;

        public struct SurfaceInput
        {
            public float2 uv;
            public float4 tint;
        }

        public interface ISurface
        {
            float4 GetBaseColor(SurfaceInput input) { return input.tint; }
        }
        """;

    // The lit pass template: generic over ISurface, no knowledge of the
    // surface's type name.
    private const string LitTemplate = """
        #language slang 2025
        module disc_template;

        import disc_contract;

        cbuffer camera : register(b0, space0)
        {
            float4x4 viewProjection;
        }

        public struct LitV2F
        {
            public float4 position : SV_POSITION;
            public float2 uv : TEXCOORD0;
        }

        [shader("vertex")]
        public LitV2F MainVS<T : ISurface>(float3 position : POSITION, float2 uv : TEXCOORD0)
        {
            LitV2F output;
            output.position = mul(viewProjection, float4(position, 1.0));
            output.uv = uv;
            return output;
        }

        [shader("fragment")]
        public float4 MainPS<T : ISurface>(LitV2F input) : SV_TARGET
        {
            T surface = T();
            SurfaceInput surfaceInput;
            surfaceInput.uv = input.uv;
            surfaceInput.tint = float4(1, 1, 1, 1);
            return surface.GetBaseColor(surfaceInput);
        }
        """;

    // The depth template with a value-specialization axis, to prove Kind=Type
    // and Kind=Expr arguments mix in one Specialize call.
    private const string ShadowTemplate = """
        #language slang 2025
        module disc_shadow_template;

        import disc_contract;

        cbuffer light : register(b0, space0)
        {
            float4x4 lightViewProjection;
        }

        public struct DepthV2F
        {
            public float4 position : SV_POSITION;
            public float2 uv : TEXCOORD0;
        }

        [shader("vertex")]
        public DepthV2F MainVS<T : ISurface>(float3 position : POSITION, float2 uv : TEXCOORD0)
        {
            DepthV2F output;
            output.position = mul(lightViewProjection, float4(position, 1.0));
            output.uv = uv;
            return output;
        }

        [shader("fragment")]
        public void MainPS<T : ISurface, let AlphaTest : bool>(DepthV2F input)
        {
            if (AlphaTest)
            {
                T surface = T();
                SurfaceInput surfaceInput;
                surfaceInput.uv = input.uv;
                surfaceInput.tint = float4(1, 1, 1, 1);
                if (surface.GetBaseColor(surfaceInput).a < 0.5)
                    discard;
            }
        }
        """;

    // A surface whose type is deliberately NOT named "Surface", plus a public
    // struct that does not conform — discovery must find one, skip the other.
    private const string MossySurface = """
        #language slang 2025
        module disc_mossy;

        import disc_contract;

        [[vk::binding(0, 1)]] Texture2D<float4> mossTexture;
        [[vk::binding(1, 1)]] SamplerState mossSampler;

        [[vk::binding(2, 1)]] cbuffer mossParams
        {
            float intensity;
        }

        public struct MossyRock : ISurface
        {
            public override float4 GetBaseColor(SurfaceInput input)
            {
                return mossTexture.Sample(mossSampler, input.uv) * intensity;
            }
        }

        public struct NotASurface
        {
            public float dummy;
        }
        """;

    // Two conformers in one module — discovery must surface the ambiguity
    // (both candidates) instead of silently picking one.
    private const string TwoConformers = """
        #language slang 2025
        module disc_two;

        import disc_contract;

        public struct VariantA : ISurface { }

        public struct VariantB : ISurface { }
        """;

    // Generic aggregation: Stack<A, B> forwards to both layers. If
    // spReflection_specializeType can apply Stack<Snow, Base>, multi-library
    // composition needs no generated wrapper module at all.
    private const string Layers = """
        #language slang 2025
        module disc_layers;

        import disc_contract;

        [[vk::binding(0, 1)]] Texture2D<float4> snowTex;
        [[vk::binding(1, 1)]] SamplerState snowSampler;
        [[vk::binding(2, 1)]] Texture2D<float4> baseTex;
        [[vk::binding(3, 1)]] SamplerState baseSampler;

        public struct Snow : ISurface
        {
            public override float4 GetBaseColor(SurfaceInput input)
            {
                return snowTex.Sample(snowSampler, input.uv);
            }
        }

        public struct Base : ISurface
        {
            public override float4 GetBaseColor(SurfaceInput input)
            {
                return baseTex.Sample(baseSampler, input.uv);
            }
        }

        public struct Stack<A : ISurface, B : ISurface> : ISurface
        {
            public override float4 GetBaseColor(SurfaceInput input)
            {
                A a = A();
                B b = B();
                return a.GetBaseColor(input) + b.GetBaseColor(input);
            }
        }
        """;

    [Test]
    public void ModuleDecl_EnumeratesStructs_AndIsSubTypeSelectsTheConformer()
    {
        using SlangCompiler compiler = new();
        using SlangCompileSession session = compiler.CreateSession(OptionsFor(Files()));
        SlangModuleHandle mossy = session.LoadModuleFromSource("disc_mossy", "disc_mossy.slang", MossySurface);

        // 1. The module's own declaration tree, and its struct children.
        IntPtr moduleDecl = mossy.Native.GetModuleReflectionDecl();
        Assert.That(SlangNative.spReflectionDecl_getKind(moduleDecl),
            Is.EqualTo(SlangNative.SLANG_DECL_KIND_MODULE));
        List<(string Name, IntPtr Type)> structs = EnumerateStructDecls(moduleDecl);
        Assert.That(structs.Select(s => s.Name), Is.EquivalentTo(new[] { "MossyRock", "NotASurface" }));

        // 2. The contract interface type, by name on the surface module's own
        //    layout (the interface is visible through the import).
        IntPtr layout = mossy.Native.AsComponentType().GetLayout(out string? layoutDiag);
        Assert.That(layout, Is.Not.EqualTo(IntPtr.Zero), layoutDiag ?? "no diagnostics");
        IntPtr interfaceType = FindTypeByName(layout, "ISurface");
        Assert.That(interfaceType, Is.Not.EqualTo(IntPtr.Zero), "ISurface must resolve on the surface module's layout");
        Assert.That(SlangNative.spReflectionType_GetKind(interfaceType),
            Is.EqualTo(SlangNative.SLANG_TYPE_KIND_INTERFACE));

        // 3. Conformance is a subtype query — the discovery key is the
        //    contract, not any name of the implementation.
        IntPtr mossyType = structs.Single(s => s.Name == "MossyRock").Type;
        IntPtr notSurfaceType = structs.Single(s => s.Name == "NotASurface").Type;
        Assert.That(SlangNative.spReflection_isSubType(layout, mossyType, interfaceType), Is.True);
        Assert.That(SlangNative.spReflection_isSubType(layout, notSurfaceType, interfaceType), Is.False);
    }

    [Test]
    public void TemplateDecl_ExposesItsGenericContract_InterfaceTypeIncluded()
    {
        using SlangCompiler compiler = new();
        using SlangCompileSession session = compiler.CreateSession(OptionsFor(Files()));
        SlangModuleHandle template = session.LoadModuleFromSource("disc_template", "disc_template.slang", LitTemplate);

        // The generic entry points appear as Generic decls in the module tree;
        // their type parameters carry the constraint — the interface type.
        IntPtr moduleDecl = template.Native.GetModuleReflectionDecl();
        List<IntPtr> generics =
        [
            .. ChildDecls(moduleDecl).Where(child =>
                SlangNative.spReflectionDecl_getKind(child) == SlangNative.SLANG_DECL_KIND_GENERIC),
        ];
        Assert.That(generics, Has.Count.GreaterThanOrEqualTo(2),
            "the template's generic entry points must appear as Generic decls");

        IntPtr generic = SlangNative.spReflectionDecl_castToGeneric(generics[0]);
        Assert.That(generic, Is.Not.EqualTo(IntPtr.Zero));
        uint typeParamCount = SlangNative.spReflectionGeneric_GetTypeParameterCount(generic);
        Assert.That(typeParamCount, Is.GreaterThanOrEqualTo(1), "MainVS<T> must expose its type parameter");

        IntPtr typeParam = SlangNative.spReflectionGeneric_GetTypeParameter(generic, 0);
        Assert.That(SlangNative.StringFromPtr(SlangNative.spReflectionVariable_GetName(typeParam)), Is.EqualTo("T"));
        Assert.That(SlangNative.spReflectionGeneric_GetTypeParameterConstraintCount(generic, typeParam),
            Is.GreaterThanOrEqualTo(1));

        IntPtr constraint = SlangNative.spReflectionGeneric_GetTypeParameterConstraintType(generic, typeParam, 0);
        Assert.That(constraint, Is.Not.EqualTo(IntPtr.Zero));
        Assert.That(SlangNative.spReflectionType_GetKind(constraint),
            Is.EqualTo(SlangNative.SLANG_TYPE_KIND_INTERFACE));
        Assert.That(SlangNative.StringFromPtr(SlangNative.spReflectionType_GetName(constraint)),
            Does.Contain("ISurface"));
    }

    [Test]
    public void DiscoveredType_SpecializesByTypeKind_AndCompiles()
    {
        using SlangCompiler compiler = new();
        using SlangCompileSession session = compiler.CreateSession(OptionsFor(Files()));
        SlangModuleHandle template = session.LoadModuleFromSource("disc_template", "disc_template.slang", LitTemplate);
        SlangModuleHandle mossy = session.LoadModuleFromSource("disc_mossy", "disc_mossy.slang", MossySurface);

        // Discovery, exactly as production would run it: contract from the
        // template's own generic constraint, conformers from the surface
        // module's decl tree, one subtype query each.
        IntPtr contract = TemplateContract(template);
        List<(string Name, IntPtr Type)> conformers = Conformers(session, mossy, contract);
        Assert.That(conformers.Select(c => c.Name), Is.EqualTo(new[] { "MossyRock" }));
        IntPtr mossyType = conformers[0].Type;

        // This module deliberately exports no 'Surface' type; discovery must
        // work regardless of the type name.
        IntPtr surfaceModuleLayout = mossy.Native.AsComponentType().GetLayout(out _);
        Assert.That(FindTypeByName(surfaceModuleLayout, "Surface"), Is.EqualTo(IntPtr.Zero),
            "the module deliberately exports no 'Surface' type");

        // Specialization by TypeReflection (Kind=Type), one argument per entry
        // point in entry order — the lit template has MainVS<T>, MainPS<T>.
        SlangSpecializationArg[] args = [SlangSpecializationArg.FromType(mossyType), SlangSpecializationArg.FromType(mossyType)];
        (byte[][] code, ShaderReflection reflection) = SpecializeCompile(session, template, mossy, _ => args);
        Assert.Multiple(() =>
        {
            Assert.That(code, Has.Length.EqualTo(2));
            foreach (byte[] entry in code)
                Assert.That(entry[0..4], Is.EqualTo(new byte[] { 0x03, 0x02, 0x23, 0x07 }), "SPIR-V magic");
            Assert.That(reflection.TryGetResourceId("mossTexture", out _), Is.True,
                "the discovered type's resources must be in the composed layout");
            Assert.That(reflection.TryGetResourceId("mossParams", out _), Is.True);
        });
    }

    [Test]
    public void DiscoveredType_MixesWithValueSpecialization()
    {
        using SlangCompiler compiler = new();
        using SlangCompileSession session = compiler.CreateSession(OptionsFor(Files()));
        SlangModuleHandle template = session.LoadModuleFromSource(
            "disc_shadow_template", "disc_shadow_template.slang", ShadowTemplate);
        SlangModuleHandle mossy = session.LoadModuleFromSource("disc_mossy", "disc_mossy.slang", MossySurface);

        IntPtr mossyType = Conformers(session, mossy, TemplateContract(template)).Single().Type;

        // MainVS<T>, MainPS<T, let AlphaTest : bool> — the flat argument list
        // concatenates per entry: [type, type, value].
        using SlangPinnedUtf8 alphaTest = new("true");
        SlangSpecializationArg[] args =
        [
            SlangSpecializationArg.FromType(mossyType),
            SlangSpecializationArg.FromType(mossyType),
            SlangSpecializationArg.FromExpr(alphaTest.Pointer),
        ];
        (byte[][] code, ShaderReflection reflection) = SpecializeCompile(session, template, mossy, _ => args);
        Assert.Multiple(() =>
        {
            Assert.That(code, Has.Length.EqualTo(2));
            foreach (byte[] entry in code)
                Assert.That(entry[0..4], Is.EqualTo(new byte[] { 0x03, 0x02, 0x23, 0x07 }), "SPIR-V magic");
            Assert.That(reflection.TryGetResourceId("mossTexture", out _), Is.True,
                "AlphaTest=true must keep the alpha-test fetch in the layout");
        });
    }

    [Test]
    public void TwoConformers_AreBothDiscovered()
    {
        using SlangCompiler compiler = new();
        using SlangCompileSession session = compiler.CreateSession(OptionsFor(Files()));
        SlangModuleHandle template = session.LoadModuleFromSource("disc_template", "disc_template.slang", LitTemplate);
        SlangModuleHandle two = session.LoadModuleFromSource("disc_two", "disc_two.slang", TwoConformers);

        List<(string Name, IntPtr Type)> conformers = Conformers(session, two, TemplateContract(template));
        Assert.That(conformers.Select(c => c.Name), Is.EquivalentTo(new[] { "VariantA", "VariantB" }),
            "an ambiguous module must report both — the engine turns this into a precise error");
    }

    // ── production-path tests: GetComposedProgram runs the discovery itself ──

    [Test]
    public void GetComposedProgram_Discovery_ComposesARenamedConformer()
    {
        using SlangModuleSystem system = new(OptionsFor(Files()), null);

        // The surface type is deliberately NOT named "Surface"; discovery must
        // work regardless of the type name.
        using SlangProgram program = system.GetComposedProgram(
            "disc_template", "disc_mossy", []);
        Assert.Multiple(() =>
        {
            Assert.That(program.EntryPoints, Has.Count.EqualTo(2));
            Assert.That(program.Reflection.TryGetResourceId("mossTexture", out _), Is.True);
            Assert.That(program.Reflection.TryGetResourceId("mossParams", out _), Is.True);
        });
    }

    [Test]
    public void GetComposedProgram_ZeroConformers_FailsNamingTheContract()
    {
        using SlangModuleSystem system = new(OptionsFor(Files()), null);

        // disc_contract exports the interface but no implementation of it.
        ShaderCompilationException? error = Assert.Throws<ShaderCompilationException>(() =>
        {
            using SlangProgram program = system.GetComposedProgram("disc_template", "disc_contract", []);
            _ = program.EntryPoints;
        });
        Assert.That(error!.Message, Does.Contain("disc_contract"));
        Assert.That(error.Message, Does.Contain("ISurface"),
            "the error must name the contract the module fails to implement");
    }

    [Test]
    public void GetComposedProgram_MultipleConformers_FailsListingTheCandidates()
    {
        using SlangModuleSystem system = new(OptionsFor(Files()), null);

        ShaderCompilationException? error = Assert.Throws<ShaderCompilationException>(() =>
        {
            using SlangProgram program = system.GetComposedProgram("disc_template", "disc_two", []);
            _ = program.EntryPoints;
        });
        Assert.That(error!.Message, Does.Contain("VariantA"));
        Assert.That(error.Message, Does.Contain("VariantB"),
            "an ambiguous module must fail listing every candidate");
    }

    [Test]
    public void SpecializeType_BuildsGenericAggregateWithoutWrapperModule()
    {
        using SlangCompiler compiler = new();
        using SlangCompileSession session = compiler.CreateSession(OptionsFor(Files()));
        SlangModuleHandle template = session.LoadModuleFromSource("disc_template", "disc_template.slang", LitTemplate);
        SlangModuleHandle layers = session.LoadModuleFromSource("disc_layers", "disc_layers.slang", Layers);

        // Stack<Snow, Base> without a generated wrapper module declaring an
        // aggregate type. Two candidate mechanisms, probed in order; the
        // spike's question is which of them slang actually supports.
        using SlangPinnedUtf8 aggregateExpr = new("Stack<Snow, Base>");
        SlangSpecializationArg[] exprArgs =
        [
            SlangSpecializationArg.FromExpr(aggregateExpr.Pointer),
            SlangSpecializationArg.FromExpr(aggregateExpr.Pointer),
        ];
        (byte[][] code, ShaderReflection reflection) = SpecializeCompile(session, template, layers, programLayout =>
        {
            // Probe 1: does the type-string parser itself apply generics?
            IntPtr stacked = FindTypeByName(programLayout, "Stack<Snow, Base>");
            if (stacked == IntPtr.Zero)
            {
                // Probe 2: the specialization-argument expression grammar.
                TestContext.Out.WriteLine("probe 1 (FindTypeByName 'Stack<Snow, Base>'): no; trying expr args");
                return exprArgs;
            }

            TestContext.Out.WriteLine("probe 1 (FindTypeByName 'Stack<Snow, Base>'): yes");
            return [SlangSpecializationArg.FromType(stacked), SlangSpecializationArg.FromType(stacked)];
        });
        Assert.Multiple(() =>
        {
            Assert.That(code, Has.Length.EqualTo(2));
            foreach (byte[] entry in code)
                Assert.That(entry[0..4], Is.EqualTo(new byte[] { 0x03, 0x02, 0x23, 0x07 }), "SPIR-V magic");
            Assert.That(reflection.TryGetResourceId("snowTex", out _), Is.True,
                "both layers' resources must survive the aggregation");
            Assert.That(reflection.TryGetResourceId("baseTex", out _), Is.True);
        });
    }

    // ── discovery helpers (the shape production code would take) ──

    /// <summary>The contract interface type of a template's first generic entry point.</summary>
    private static IntPtr TemplateContract(SlangModuleHandle template)
    {
        IntPtr moduleDecl = template.Native.GetModuleReflectionDecl();
        IntPtr genericDecl = ChildDecls(moduleDecl).First(child =>
            SlangNative.spReflectionDecl_getKind(child) == SlangNative.SLANG_DECL_KIND_GENERIC);
        IntPtr generic = SlangNative.spReflectionDecl_castToGeneric(genericDecl);
        IntPtr typeParam = SlangNative.spReflectionGeneric_GetTypeParameter(generic, 0);
        return SlangNative.spReflectionGeneric_GetTypeParameterConstraintType(generic, typeParam, 0);
    }

    /// <summary>The surface module's public structs that conform to the contract interface.</summary>
    private static List<(string Name, IntPtr Type)> Conformers(
        SlangCompileSession session, SlangModuleHandle surface, IntPtr contract)
    {
        _ = session;
        IntPtr layout = surface.Native.AsComponentType().GetLayout(out _);
        return EnumerateStructDecls(surface.Native.GetModuleReflectionDecl())
            .Where(candidate => SlangNative.spReflection_isSubType(layout, candidate.Type, contract))
            .ToList();
    }    private static List<(string Name, IntPtr Type)> EnumerateStructDecls(IntPtr moduleDecl)
    {
        List<(string, IntPtr)> structs = [];
        foreach (IntPtr child in ChildDecls(moduleDecl))
        {
            if (SlangNative.spReflectionDecl_getKind(child) != SlangNative.SLANG_DECL_KIND_STRUCT)
            {
                continue;
            }
            string name = SlangNative.StringFromPtr(SlangNative.spReflectionDecl_getName(child)) ?? "?";
            // Slang synthesizes parameter-group structs (e.g.
            // SLANG_ParameterGroup__mossParams) for cbuffer declarations — not
            // authored surface types.
            if (name.StartsWith("SLANG_", StringComparison.Ordinal))
            {
                continue;
            }
            structs.Add((name, SlangNative.spReflection_getTypeFromDecl(child)));
        }
        return structs;
    }

    private static List<IntPtr> ChildDecls(IntPtr decl)
    {
        List<IntPtr> children = [];
        uint count = SlangNative.spReflectionDecl_getChildrenCount(decl);
        for (uint i = 0; i < count; i++)
        {
            children.Add(SlangNative.spReflectionDecl_getChild(decl, i));
        }
        return children;
    }

    private static IntPtr FindTypeByName(IntPtr layout, string name)
    {
        using SlangPinnedUtf8 pinned = new(name);
        return SlangNative.spReflection_FindTypeByName(layout, pinned.Pointer);
    }

    /// <summary>
    /// Manual composed compile — the engine's CompileComposed path with caller-chosen
    /// specialization args: composite [template, surface, entries...] → Specialize →
    /// Link → entry code + reflection. The arg builder receives the unspecialized
    /// composite's layout (type lookup / type-level specialization happen there).
    /// </summary>
    private static (byte[][] Code, ShaderReflection Reflection) SpecializeCompile(
        SlangCompileSession session, SlangModuleHandle template, SlangModuleHandle surface,
        Func<IntPtr, SlangSpecializationArg[]> argBuilder)
    {
        SlangModule templateModule = template.Native;
        SlangModule surfaceModule = surface.Native;
        int entryCount = templateModule.DefinedEntryPointCount;
        SlangComponentType[] components = new SlangComponentType[entryCount + 2];
        components[0] = templateModule.AsComponentType();
        components[1] = surfaceModule.AsComponentType();
        for (int i = 0; i < entryCount; i++)
        {
            SlangEntryPoint? entry = templateModule.GetDefinedEntryPoint(i);
            Assert.That(entry, Is.Not.Null, $"template entry point {i}");
            components[i + 2] = entry!.AsComponentType();
        }

        SlangComponentType composite = session.Native.CreateCompositeComponentType(components, out string? compositeDiag);
        try
        {
            SlangSpecializationArg[] args = argBuilder(composite.GetLayout(out string? compositeLayoutDiag));
            Assert.That(args, Has.Length.GreaterThan(0));
            SlangComponentType specialized = SlangSession.Specialize(composite, args, out string? specDiag);
            try
            {
                SlangComponentType linked = specialized.Link(out string? linkDiag);
                try
                {
                    IntPtr layout = linked.GetLayout(out string? layoutDiag);
                    Assert.That(layout, Is.Not.EqualTo(IntPtr.Zero),
                        string.Join("; ", new[] { compositeDiag, compositeLayoutDiag, specDiag, linkDiag, layoutDiag }.Where(d => d != null)));

                    byte[][] code = new byte[entryCount][];
                    for (int i = 0; i < entryCount; i++)
                        code[i] = linked.GetEntryPointCode(i, out _);
                    return (code, SlangReflectionReader.BuildReflectionInfo(layout));
                }
                finally
                {
                    linked.Release();
                }
            }
            finally
            {
                specialized.Release();
            }
        }
        finally
        {
            for (int i = 2; i < components.Length; i++)
                components[i]?.Release();
            composite.Release();
        }
    }

    private static Dictionary<string, string> Files() => new()
    {
        ["disc_contract.slang"] = Contract,
        ["disc_template.slang"] = LitTemplate,
        ["disc_shadow_template.slang"] = ShadowTemplate,
        ["disc_mossy.slang"] = MossySurface,
        ["disc_two.slang"] = TwoConformers,
        ["disc_layers.slang"] = Layers,
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
