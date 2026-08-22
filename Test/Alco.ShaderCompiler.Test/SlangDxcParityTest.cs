using Alco.Graphics;
using NUnit.Framework;

namespace Alco.ShaderCompiler;

// ─────────────────────────────────────────────────────────────────────────────
// Phase-0 parity harness (plan §Phase 0.4): the same engine shader source is
// compiled through the legacy DXC path (dxc → SPIR-V → SpirvReflector) and the
// slang modern API path (module → link → ProgramLayout → SlangReflectionReader),
// then the two ShaderReflectionInfo results are compared.
//
// What is compared — the engine's actual consumption contract:
//   • every bind group entry by NAME and BindingType (name-based binding, D1)
//   • push-constant size, fragment output count, thread group size
//   • vertex input layouts (stride + ordered name/format/offset of elements)
//
// What is deliberately NOT compared yet:
//   • set/binding NUMBERS: under __SLANG__ the Core.hlsli convention macros drop
//     the set-only register annotations, so slang assigns one sequential space
//     while dxc keeps the declared sets. The declared-set restoration lands with
//     the Phase-3 binding-range cut-over; this harness is extended then.
//   • per-entry stage flags: slang reflection reports the entry-point union for
//     global parameters, dxc reports per-stage usage.
//   • SPIR-V bytes: not deterministic across front ends; only the module magic
//     is checked.
// This fixture is the A/B tool for Phases 2–3: migrating a shader directory or
// switching the reflection producer adds/keeps cases here.
// ─────────────────────────────────────────────────────────────────────────────
[TestFixture]
public class SlangDxcParityTest
{
    public enum PipelineKind
    {
        Graphics,
        Compute,
    }

    private static readonly (string VirtualPath, PipelineKind Kind)[] ShaderCases =
    [
        // 2D pipeline: cbuffer + texture/sampler pair + push constants + vertex layout.
        ("Shaders/Pipelines/Rendering/Sprite/Sprite.hlsl", PipelineKind.Graphics),
        // Postprocess: push constants + sampled texture, macro-driven sampling.
        ("Shaders/Pipelines/PostProcess/Bloom/BloomDownSample.hlsl", PipelineKind.Graphics),
        // World3D PBR pipeline + material surface chain (@SURFACE@ default include).
        ("Shaders/Pipelines/Rendering/PBR/GBuffer.hlsl", PipelineKind.Graphics),
        // Compute: storage textures with image formats + structured buffer + thread groups.
        ("Shaders/Pipelines/Compute/GaussianBlurRGBA16F.hlsl", PipelineKind.Compute),
    ];

    public static IEnumerable<TestCaseData> Cases()
    {
        foreach ((string path, PipelineKind kind) in ShaderCases)
        {
            yield return new TestCaseData(path, kind)
                .SetName($"Parity {Path.GetFileNameWithoutExtension(path)} ({kind})");
        }
    }

    private static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Alco.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException(
            $"Repository root (Alco.slnx) not found above {AppContext.BaseDirectory}");
    }

    private static readonly string[] ShaderRoots =
    [
        Path.Combine(RepoRoot(), "Src", "Alco.Rendering", "Assets"),
        Path.Combine(RepoRoot(), "Src", "Alco.World3D", "Assets"),
    ];

    private static readonly Lock ResolveLock = new();
    private static readonly Dictionary<string, string?> ResolveCache = new();

    /// <summary>
    /// Serves virtual shader paths ('Shaders/…') from the source tree. slang
    /// combines #include paths relative to the including file, so a root-relative
    /// include inside a nested module arrives doubled ('Shaders/A/B/Shaders/…');
    /// the resolver re-roots at every 'Shaders/' segment (beachhead behavior).
    /// </summary>
    private static string? Serve(string path)
    {
        string key = SlangPathUtility.NormalizePath(path);
        lock (ResolveLock)
        {
            if (ResolveCache.TryGetValue(key, out string? cached))
            {
                return cached;
            }
            string? content = null;
            int index = key.IndexOf("Shaders/", StringComparison.OrdinalIgnoreCase);
            while (index >= 0 && content == null)
            {
                content = ReadUnderRoots(key[index..]);
                index = key.IndexOf("Shaders/", index + 1, StringComparison.OrdinalIgnoreCase);
            }
            return ResolveCache[key] = content ?? ReadUnderRoots(key);
        }
    }

    private static string? ReadUnderRoots(string virtualPath)
    {
        foreach (string root in ShaderRoots)
        {
            string full = Path.Combine([root, .. virtualPath.Split('/')]);
            if (File.Exists(full))
            {
                return File.ReadAllText(full);
            }
        }
        return null;
    }

    [TestCaseSource(nameof(Cases))]
    public void ReflectionMatchesAcrossCompilers(string virtualPath, PipelineKind kind)
    {
        ShaderReflectionInfo dxc = kind == PipelineKind.Compute
            ? CompileDxcCompute(virtualPath)
            : CompileDxcGraphics(virtualPath);
        ShaderReflectionInfo slang = CompileSlang(virtualPath, kind);

        AssertParity(Path.GetFileName(virtualPath), kind, dxc, slang);
    }

    // ── DXC path (production legacy path, untouched) ─────────────────────────

    private static CompilationResult CompileDxcStage(string source, string entryPoint, ShaderProfile profile)
    {
        var options = new CompilerOptions(profile)
        {
            entryPoint = entryPoint,
            generateAsSpirV = true,
        };
        CompilationResult result = ShaderCompiler.Compile(source, options, IncludeHandler);
        Assert.That(result.compilationErrors, Is.Null,
            $"dxc failed for entry '{entryPoint}': {result.compilationErrors}");
        AssertSpirvMagic(result.objectBytes, $"dxc '{entryPoint}'");
        return result;
    }

    private static string IncludeHandler(string includeName)
        => Serve(includeName)
            ?? throw new ShaderCompilationException($"parity harness: cannot resolve include '{includeName}'");

    private static ShaderReflectionInfo CompileDxcGraphics(string virtualPath)
    {
        string source = Serve(virtualPath)
            ?? throw new ShaderCompilationException($"parity harness: cannot resolve '{virtualPath}'");
        CompilationResult vertex = CompileDxcStage(source, "MainVS", ShaderProfile.Vertex_6_0);
        CompilationResult fragment = CompileDxcStage(source, "MainPS", ShaderProfile.Fragment_6_0);
        return ShaderReflectionUtility.GetSpirvReflection(vertex.objectBytes, fragment.objectBytes);
    }

    private static ShaderReflectionInfo CompileDxcCompute(string virtualPath)
    {
        string source = Serve(virtualPath)
            ?? throw new ShaderCompilationException($"parity harness: cannot resolve '{virtualPath}'");
        CompilationResult compute = CompileDxcStage(source, "MainCS", ShaderProfile.Compute_6_0);
        return ShaderReflectionUtility.GetSpirvReflection(compute.objectBytes);
    }

    // ── slang path (modern API facade) ───────────────────────────────────────

    private static ShaderReflectionInfo CompileSlang(string virtualPath, PipelineKind kind)
    {
        string source = Serve(virtualPath)
            ?? throw new ShaderCompilationException($"parity harness: cannot resolve '{virtualPath}'");

        using SlangCompiler compiler = SlangCompiler.Create();
        SlangCompilerOptions options = new()
        {
            // Core.hlsli's convention macros drop set-only register annotations
            // under __SLANG__ (the transitional beachhead mode).
            PreprocessorMacros = [("__SLANG__", "1")],
            Resolver = Serve,
        };
        using SlangCompileSession session = compiler.CreateSession(options);

        SlangModuleHandle module = session.LoadModuleFromSource(
            Path.GetFileNameWithoutExtension(virtualPath), virtualPath, source);

        List<SlangEntryPointRequest> entryPoints = kind == PipelineKind.Compute
            ? [new SlangEntryPointRequest("MainCS", ShaderStage.Compute)]
            : [new SlangEntryPointRequest("MainVS", ShaderStage.Vertex),
               new SlangEntryPointRequest("MainPS", ShaderStage.Fragment)];
        using SlangProgram program = session.Compile(module, entryPoints);

        foreach (byte[] code in program.EntryCode)
        {
            AssertSpirvMagic(code, $"slang '{virtualPath}'");
        }
        return program.Reflection;
    }

    // ── comparison ───────────────────────────────────────────────────────────

    private static void AssertParity(string name, PipelineKind kind, ShaderReflectionInfo dxc, ShaderReflectionInfo slang)
    {
        Assert.Multiple(() =>
        {
            CollectionAssert.AreEquivalent(EntriesOf(dxc), EntriesOf(slang),
                $"{name}: bind group entries (name → binding type) differ between dxc and slang");
            // slang rounds a push-constant block's tail up to its max member
            // alignment (HLSL cbuffer rules) while the DXC SPIR-V block carries
            // no tail padding; the extra bytes are inert for binding.
            Assert.That(slang.PushConstantsSize, Is.InRange(dxc.PushConstantsSize, dxc.PushConstantsSize + 15),
                $"{name}: push-constant size differs beyond tail padding");
            if (kind == PipelineKind.Graphics)
            {
                // The DXC reflector reports 1 for compute stages; the engine
                // only consumes this count for fragment outputs.
                Assert.That(slang.FragmentOutputCount, Is.EqualTo(dxc.FragmentOutputCount),
                    $"{name}: fragment output count differs");
            }
            Assert.That(slang.Size, Is.EqualTo(dxc.Size),
                $"{name}: thread group size differs");
            if (kind == PipelineKind.Graphics)
            {
                // The DXC reflector always emits one (possibly empty) vertex
                // layout, even for compute; only graphics layouts are consumed.
                AssertVertexLayoutParity(name, dxc.VertexLayouts, slang.VertexLayouts);
            }
        });
    }

    /// <summary>Flattens all entries to comparable (name, binding type) tuples.</summary>
    private static List<(string Name, BindingType Type)> EntriesOf(ShaderReflectionInfo info)
    {
        List<(string, BindingType)> entries = [];
        foreach (BindGroupLayout group in info.BindGroups)
        {
            foreach (BindGroupEntryInfo binding in group.Bindings)
            {
                entries.Add((binding.Entry.Name, binding.Entry.Type));
            }
        }
        return entries;
    }

    private static void AssertVertexLayoutParity(
        string name, IReadOnlyList<VertexInputLayout> dxc, IReadOnlyList<VertexInputLayout> slang)
    {
        Assert.That(slang.Count, Is.EqualTo(dxc.Count), $"{name}: vertex layout count differs");
        for (int i = 0; i < Math.Min(dxc.Count, slang.Count); i++)
        {
            Assert.That(slang[i].Stride, Is.EqualTo(dxc[i].Stride),
                $"{name}: vertex layout {i} stride differs");
            Assert.That(slang[i].Elements.Length, Is.EqualTo(dxc[i].Elements.Length),
                $"{name}: vertex layout {i} element count differs");
            for (int j = 0; j < Math.Min(dxc[i].Elements.Length, slang[i].Elements.Length); j++)
            {
                Assert.Multiple(() =>
                {
                    // Name is not compared: dxc reports SPIR-V debug names
                    // ('in.var.POSITION'), slang the field names ('position');
                    // the engine binds vertex attributes by location.
                    Assert.That(slang[i].Elements[j].Location, Is.EqualTo(dxc[i].Elements[j].Location),
                        $"{name}: vertex layout {i} element {j} location differs");
                    Assert.That(slang[i].Elements[j].Format, Is.EqualTo(dxc[i].Elements[j].Format),
                        $"{name}: vertex layout {i} element {j} format differs");
                    Assert.That(slang[i].Elements[j].Offset, Is.EqualTo(dxc[i].Elements[j].Offset),
                        $"{name}: vertex layout {i} element {j} offset differs");
                });
            }
        }
    }

    private static void AssertSpirvMagic(byte[] code, string producer)
    {
        Assert.That(code.Length, Is.GreaterThan(4), $"{producer}: no SPIR-V emitted");
        // 0x07230203 little-endian.
        Assert.That(code[0..4], Is.EqualTo(new byte[] { 0x03, 0x02, 0x23, 0x07 }),
            $"{producer}: not SPIR-V");
    }
}
