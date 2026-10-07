#nullable enable

using NUnit.Framework;
using Alco.Graphics;

namespace Alco.Rendering.Test;

// ─────────────────────────────────────────────────────────────────────────────
// Slang-mode ValidateShader: every engine .slang module under
// Alco.Rendering's Shaders tree must load through the module system and
// link every [shader(...)] entry point headlessly. File-tree resolver mirrors
// the engine's asset resolver conventions (module-name matching).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Validates engine shader modules and their material compositions.</summary>
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

    /// <summary>Returns every engine shader module that owns entry points.</summary>
    public static IEnumerable<TestCaseData> ModuleCases()
    {
        string root = Path.Combine(RepoRoot(), "Src", "Alco.Rendering", "Assets", "Shaders");
        foreach (string file in Directory.GetFiles(root, "*.slang", SearchOption.AllDirectories))
        {
            // Libs are imported and materials are composed, not entry modules —
            // only pass modules own entry points; their file base name is the
            // module identity (materials get their link coverage through
            // TemplateSurfaces composition instead).
            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (relative.StartsWith("Libs/", StringComparison.OrdinalIgnoreCase) ||
                relative.StartsWith("Materials/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            string moduleName = Path.GetFileNameWithoutExtension(file);
            yield return new TestCaseData(moduleName, file).SetName($"{{m}}({moduleName})");
        }
    }

    // Modules with generic entry points: a generic
    // entry point cannot link unspecialized, so the no-argument asset-load
    // sweep (Engine's ValidateAllShaders) excludes them — this table is their
    // only link/codegen coverage, through ONE representative argument set per
    // module. Enumerating every value is deliberately not done: slang's
    // front-end type-checks every branch of a generic body at module load,
    // independent of the argument values (link-time specialization), so values
    // only differ in how already-validated IR constant-folds. What the single
    // link proves is the stages after the front-end: specialization argument
    // matching (arity/type), linking, layout validation and target codegen.
    //   FXAA: <let Quality : int, let Mode : int> — each mode linked: the mode
    //   axis changes which branches stay live after constant folding, so every
    //   mode's codegen is distinct, Sprite: <let Repeated : bool>,
    //   TextureCompressBc1/TextureCompressBc3: <let IsSRGB : bool>,
    //   TileInstanced: VertexMain<let IsFacade : bool>, PixelMain<let Bombing :
    //   bool> — args map to entry points in definition order.
    private static readonly IReadOnlyDictionary<string, string[][]> Specializations =
        new Dictionary<string, string[][]>
        {
            ["FXAA"] = [["1", "0"], ["1", "1"], ["1", "2"]],
            ["Sprite"] = [["false"]],
            ["TextureCompressBc1"] = [["false"]],
            ["TextureCompressBc3"] = [["false"]],
            ["TileInstanced"] = [["false", "false"]],
        };

    /// <summary>Compiles every entry point with representative specialization arguments.</summary>
    /// <param name="moduleName">The shader module name.</param>
    /// <param name="file">The source file represented by the test case.</param>
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
                Assert.That(code.Length, Is.GreaterThan(4), $"{moduleName}: empty SPIR-V blob");
            }
        }
        _ = file;
    }
}
