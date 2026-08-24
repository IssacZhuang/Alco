#nullable enable

using NUnit.Framework;
using Alco.ShaderCompiler;

namespace Alco.Rendering.Test;

// ─────────────────────────────────────────────────────────────────────────────
// Slang-mode ValidateShader (plan §7): every engine .slang module under
// Alco.Rendering's Shaders tree must load through the module system and
// link every [shader(...)] entry point headlessly. File-tree resolver mirrors
// the engine's asset resolver conventions (dashed module-name matching).
// ─────────────────────────────────────────────────────────────────────────────

public class ValidateSlangModules
{
    private static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Alco.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    public static IEnumerable<TestCaseData> ModuleCases()
    {
        string root = Path.Combine(RepoRoot(), "Src", "Alco.Rendering", "Assets", "Shaders");
        foreach (string file in Directory.GetFiles(root, "*.slang", SearchOption.AllDirectories))
        {
            // Libs are imported, not entry modules — only pipeline modules own
            // entry points; their file base name is the module identity.
            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (relative.StartsWith("Libs/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            string moduleName = Path.GetFileNameWithoutExtension(file).Replace('_', '-');
            yield return new TestCaseData(moduleName, file).SetName($"{{m}}({moduleName})");
        }
    }

    // Modules with generic entry points (plan D3 specialization): a generic
    // entry point cannot link unspecialized, so the no-argument asset-load
    // sweep (Engine's ValidateAllShaders) excludes them — this table is their
    // only link/codegen coverage, through ONE representative argument set per
    // module. Enumerating every value is deliberately not done: slang's
    // front-end type-checks every branch of a generic body at module load,
    // independent of the argument values (link-time specialization), so values
    // only differ in how already-validated IR constant-folds. What the single
    // link proves is the stages after the front-end: specialization argument
    // matching (arity/type), linking, layout validation and target codegen.
    //   fxaa: <let Quality : int>, sprite: <let Repeated : bool>,
    //   texture-compress-bc3: <let IsSRGB : bool>,
    //   tile-instanced: VertexMain<let IsFacade : bool>, PixelMain<let Bombing :
    //   bool> — args map to entry points in definition order.
    private static readonly IReadOnlyDictionary<string, string[][]> Specializations =
        new Dictionary<string, string[][]>
        {
            ["fxaa"] = [["1"]],
            ["sprite"] = [["false"]],
            ["texture-compress-bc3"] = [["false"]],
            ["tile-instanced"] = [["false", "false"]],
        };

    [Test]
    [TestCaseSource(nameof(ModuleCases))]
    public void Module_CompilesAllEntryPoints(string moduleName, string file)
    {
        string root = Path.Combine(RepoRoot(), "Src", "Alco.Rendering", "Assets", "Shaders");
        var files = Directory.GetFiles(root, "*.slang", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .ToList();

        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem();
        using ShaderSystem system = new(host.RenderingSystem, new SlangCompilerOptions
        {
            Resolver = ShaderModuleResolver.Create(
            path =>
            {
                string key = SlangPathUtility.NormalizePath(path);
                string candidate = Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar));
                return File.Exists(candidate) ? File.OpenRead(candidate) : null;
            },
            () => files),
        });

        system.Modules.GetOrLoadModule(moduleName);
        string[][] argSets = Specializations.TryGetValue(moduleName, out string[][]? sets)
            ? sets
            : [[]];
        foreach (string[] args in argSets)
        {
            using SlangProgram program = system.Modules.GetProgramAllEntries(moduleName, args);
            Assert.That(program.EntryPoints, Has.Count.GreaterThan(0), $"{moduleName} defines no entry points");
            Assert.That(program.EntryCode.Count, Is.EqualTo(program.EntryPoints.Count));
            foreach (ReadOnlyMemory<byte> code in program.EntryCode)
            {
                Assert.That(code.Length, Is.GreaterThan(4), "empty SPIR-V blob");
            }
        }
        _ = file;
    }
}
