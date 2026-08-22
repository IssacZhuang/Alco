#nullable enable

using NUnit.Framework;
using Alco.ShaderCompiler;

namespace Alco.World3D.Test;

// ─────────────────────────────────────────────────────────────────────────────
// Slang-mode validation of Alco.World3D's plain pipeline shaders (migration
// plan Phase 2): every converted .slang module under Alco.World3D's ShadersSlang
// tree must load through SlangModuleSystem headlessly and link every
// [shader(...)] entry point to non-empty SPIR-V. The file-tree resolver spans
// BOTH the Alco.Rendering and Alco.World3D ShadersSlang roots (World3D modules
// import alco_rendering_core) and mirrors the engine resolver's dashed
// module-name matching conventions.
// ─────────────────────────────────────────────────────────────────────────────

public class ValidateWorld3DSlangModules
{
    private static readonly string[] Roots =
    [
        Path.Combine(RepoRoot(), "Src", "Alco.Rendering", "Assets", "ShadersSlang"),
        Path.Combine(RepoRoot(), "Src", "Alco.World3D", "Assets", "ShadersSlang"),
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
    /// Resolves a slang module/import probe against both ShadersSlang trees:
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
            // pass templates (generic — no [shader] entries of their own, the
            // material compiler instantiates them) and the Phase-2 lib
            // modules converted from .slang.
            if (relative.StartsWith("Libs/", StringComparison.OrdinalIgnoreCase) ||
                relative.StartsWith("Materials/", StringComparison.OrdinalIgnoreCase) ||
                fileName.StartsWith("alco-world3d-", StringComparison.OrdinalIgnoreCase) ||
                fileName is "gbuffer.slang" or "rsm.slang" or "shadow_depth.slang" or "glass.slang")
            {
                continue;
            }

            string moduleName = Path.GetFileNameWithoutExtension(file).Replace('_', '-');
            yield return new TestCaseData(moduleName, file).SetName($"{{m}}({moduleName})");
        }
    }

    [Test]
    [TestCaseSource(nameof(ModuleCases))]
    public void Module_CompilesAllEntryPoints(string moduleName, string file)
    {
        using SlangModuleSystem system = new(new SlangCompilerOptions
        {
            EmitSpirvDirectly = false,
            Resolver = CreateResolver(),
        }, null);

        system.GetOrLoadModule(moduleName);
        using SlangProgram program = system.GetProgramAllEntries(moduleName, []);
        Assert.That(program.EntryPoints, Has.Count.GreaterThan(0), $"{moduleName} defines no entry points");
        Assert.That(program.EntryCode.Count, Is.EqualTo(program.EntryPoints.Count));
        foreach (ReadOnlyMemory<byte> code in program.EntryCode)
        {
            Assert.That(code.Length, Is.GreaterThan(4), $"{moduleName}: empty SPIR-V blob");
        }
        _ = file;
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
