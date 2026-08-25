#nullable enable

using NUnit.Framework;
using Alco.ShaderCompiler;

namespace Alco.World3D.Test;

// ─────────────────────────────────────────────────────────────────────────────
// Slang-mode validation of Alco.World3D's plain pipeline shaders (migration
// plan Phase 2): every converted .slang module under Alco.World3D's Shaders
// tree must load through SlangModuleSystem headlessly and link every
// [shader(...)] entry point to non-empty SPIR-V. The file-tree resolver spans
// BOTH the Alco.Rendering and Alco.World3D Shaders roots (World3D modules
// import alco_rendering_core) and mirrors the engine resolver's dashed
// module-name matching conventions.
// ─────────────────────────────────────────────────────────────────────────────

public class ValidateWorld3DSlangModules
{
    private static readonly string[] Roots =
    [
        Path.Combine(RepoRoot(), "Src", "Alco.Rendering", "Assets", "Shaders"),
        Path.Combine(RepoRoot(), "Src", "Alco.World3D", "Assets", "Shaders"),
    ];

    // The nine Phase-2 lib modules (converted from .slang); import-only, so
    // they own no entry points but must still load cleanly.
    private static readonly string[] LibModules =
    [
        "alco_world3d_atmosphere",
        "alco_world3d_clouds",
        "alco_world3d_geometry_normal",
        "alco_world3d_hbao_common",
        "alco_world3d_pbr_common",
        "alco_world3d_reversed_depth",
        "alco_world3d_ssr_common",
        "alco_world3d_ssr_post_common",
        "alco_world3d_voxel_common",
    ];

    private static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Alco.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static List<string> EnumerateSlangAssets()
    {
        List<string> assets = [];
        foreach (string root in Roots)
        {
            foreach (string file in Directory.GetFiles(root, "*.slang", SearchOption.AllDirectories))
            {
                assets.Add(Path.GetRelativePath(root, file).Replace('\\', '/'));
            }
        }
        assets.Sort(StringComparer.Ordinal);
        return assets;
    }

    /// <summary>
    /// Resolves a slang module/import probe against both Shaders trees:
    /// exact relative path first, then dashed EndsWith matching (the engine's
    /// ShaderModuleResolver convention — 'alco_rendering_core' answers to
    /// 'Libs/alco-rendering-core.slang' wherever it sits in the tree).
    /// </summary>
    private static Alco.ShaderCompiler.SlangFileResolver CreateResolver()
    {
        List<string> assets = EnumerateSlangAssets();
        return path =>
        {
            string key = SlangPathUtility.NormalizePath(path);
            foreach (string root in Roots)
            {
                string candidate = Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(candidate))
                {
                    return File.ReadAllText(candidate);
                }
            }

            string dashed = key.Replace('/', '-').Replace('_', '-');
            foreach (string asset in assets)
            {
                string assetDashed = asset.Replace('/', '-').Replace('_', '-');
                if (dashed.EndsWith(assetDashed, StringComparison.OrdinalIgnoreCase) ||
                    assetDashed.EndsWith(dashed, StringComparison.OrdinalIgnoreCase))
                {
                    string resolved = Roots
                        .Select(root => Path.Combine(root, asset.Replace('/', Path.DirectorySeparatorChar)))
                        .First(File.Exists);
                    return File.ReadAllText(resolved);
                }
            }
            return null;
        };
    }

    public static IEnumerable<TestCaseData> ModuleCases()
    {
        string root = Roots[1];
        foreach (string file in Directory.GetFiles(root, "*.slang", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            string fileName = Path.GetFileName(relative);

            // Import-only trees: the beachhead surface/material modules and
            // pass templates (surface-generic — the material compiler
            // instantiates them with a concrete surface type) and the Phase-2
            // lib modules converted from .slang. voxelize is a compute pass
            // template over IVoxelFeedSurface, same treatment.
            if (relative.StartsWith("Libs/", StringComparison.OrdinalIgnoreCase) ||
                relative.StartsWith("Materials/", StringComparison.OrdinalIgnoreCase) ||
                fileName.StartsWith("alco-world3d-", StringComparison.OrdinalIgnoreCase) ||
                fileName is "gbuffer.slang" or "rsm.slang" or "shadow-depth.slang" or "glass.slang"
                    or "voxelize.slang")
            {
                continue;
            }

            string moduleName = Path.GetFileNameWithoutExtension(file).Replace('_', '-');
            yield return new TestCaseData(moduleName, file).SetName($"{{m}}({moduleName})");
        }
    }

    // Modules with generic entry points (plan D3 specialization): every valid
    // specialization must link — a generic entry point cannot link unspecialized,
    // so these modules are only tested through their argument sets. Modules not
    // listed here have no generic parameters and link with empty arguments.
    //   deferred-lighting: <let DebugView : int> — one representative value (all
    //     branches are front-end checked at module load; the C# owner compiles
    //     each view on demand).
    //   volumetric-cloud-noise: <let IsDetail : bool> — false=base shape, true=detail.
    private static readonly IReadOnlyDictionary<string, string[][]> Specializations =
        new Dictionary<string, string[][]>
        {
            ["deferred-lighting"] = [["0"]],
            ["volumetric-cloud-noise"] = [["false"], ["true"]],
        };

    [Test]
    [TestCaseSource(nameof(ModuleCases))]
    public void Module_CompilesAllEntryPoints(string moduleName, string file)
    {
        using SlangModuleSystem system = new(new SlangCompilerOptions
        {
            Resolver = CreateResolver(),
        }, null);

        system.GetOrLoadModule(moduleName);
        string[][] argSets = Specializations.TryGetValue(moduleName, out string[][]? sets)
            ? sets
            : [[]];
        foreach (string[] args in argSets)
        {
            using SlangProgram program = system.GetProgramAllEntries(moduleName, args);
            Assert.That(program.EntryPoints, Has.Count.GreaterThan(0), $"{moduleName} defines no entry points");
            Assert.That(program.EntryCode.Count, Is.EqualTo(program.EntryPoints.Count));
            foreach (ReadOnlyMemory<byte> code in program.EntryCode)
            {
                Assert.That(code.Length, Is.GreaterThan(4), $"{moduleName}: empty SPIR-V blob");
            }
        }
        _ = file;
    }

    // Pass templates compose with the built-in surface module: template module,
    // companion surface module, companion type, per-entry value-specialization
    // argument sets (shadow-depth's fragment entry takes <let AlphaTest : bool>).
    private static readonly (string Template, string[][] ValueArgSets)[] PassTemplates =
    [
        ("gbuffer", [[]]),
        ("rsm", [[]]),
        ("glass", [[]]),
        ("shadow_depth", [["false"], ["true"]]),
        ("voxelize", [[]]),
    ];

    [Test]
    public void PassTemplates_ComposeWithBuiltinSurface()
    {
        using SlangModuleSystem system = new(new SlangCompilerOptions
        {
            Resolver = CreateResolver(),
        }, null);

        foreach ((string template, string[][] valueArgSets) in PassTemplates)
        {
            foreach (string[] valueArgs in valueArgSets)
            {
                using SlangProgram program = system.GetComposedProgram(
                    template, "pbr_standard", "Surface", valueArgs);
                string caseName = valueArgs.Length == 0
                    ? template
                    : $"{template}<{string.Join(",", valueArgs)}>";
                Assert.That(program.EntryPoints, Has.Count.GreaterThan(0),
                    $"{caseName}: the composition defines no entry points");
                foreach (ReadOnlyMemory<byte> code in program.EntryCode)
                {
                    Assert.That(code.Length, Is.GreaterThan(4), $"{caseName}: empty SPIR-V blob");
                }
            }
        }
    }

    public static IEnumerable<TestCaseData> LibModuleCases()
    {
        foreach (string name in LibModules)
        {
            yield return new TestCaseData(name).SetName($"{{m}}({name})");
        }
    }

    [Test]
    [TestCaseSource(nameof(LibModuleCases))]
    public void LibModule_Loads(string moduleName)
    {
        // Lib modules own no entry points; loading them still runs the full
        // parse + semantic pass, so syntax and cross-module visibility errors
        // surface here rather than in every importer.
        using SlangModuleSystem system = new(new SlangCompilerOptions { Resolver = CreateResolver() }, null);
        Assert.That(system.GetOrLoadModule(moduleName), Is.Not.Null);
    }
}
