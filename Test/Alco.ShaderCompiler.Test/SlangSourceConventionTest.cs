using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Alco.ShaderCompiler;

[TestFixture]
public partial class SlangSourceConventionTest
{
    private static string RepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Alco.slnx")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName
            ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static IEnumerable<string> ShaderFiles()
    {
        string root = RepoRoot();
        foreach (string directory in new[] { "Src", "Sandbox", "Test" })
        {
            foreach (string file in Directory.GetFiles(
                Path.Combine(root, directory), "*.slang", SearchOption.AllDirectories))
            {
                yield return file;
            }
        }
    }

    /// <summary>
    /// The engine shader trees (Sandbox samples keep their own module identity
    /// and are exempt from the naming pairing). Returns (relativePath, stem).
    /// </summary>
    private static IEnumerable<(string Relative, string Stem)> EngineShaderFiles()
    {
        string root = RepoRoot();
        foreach (string file in ShaderFiles())
        {
            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (relative.StartsWith("Sandbox"))
            {
                continue;
            }
            yield return (relative, Path.GetFileNameWithoutExtension(file));
        }
    }

    [Test]
    public void EverySlangSourcePinsLanguageAndDeclaresOneModule()
    {
        string[] files = ShaderFiles().ToArray();
        Assert.That(files, Is.Not.Empty);

        Assert.Multiple(() =>
        {
            foreach (string file in files)
            {
                string source = File.ReadAllText(file);
                MatchCollection languages = LanguageDirectiveRegex().Matches(source);
                MatchCollection modules = ModuleDeclarationRegex().Matches(source);
                string relative = Path.GetRelativePath(RepoRoot(), file);

                Assert.That(languages, Has.Count.EqualTo(1),
                    $"{relative}: expected exactly one '#language slang 2025' directive");
                Assert.That(modules, Has.Count.EqualTo(1),
                    $"{relative}: expected exactly one module declaration");
                if (languages.Count == 1 && modules.Count == 1)
                {
                    Assert.That(languages[0].Index, Is.LessThan(modules[0].Index),
                        $"{relative}: language directive must precede the module declaration");
                }
            }
        });
    }

    [Test]
    public void SourceTreesContainNoLegacyHlslOrTextualIncludes()
    {
        string root = RepoRoot();
        string[] oldShaders = new[] { "Src", "Sandbox" }
            .SelectMany(directory => Directory.GetFiles(
                Path.Combine(root, directory), "*.*", SearchOption.AllDirectories))
            .Where(file => Path.GetExtension(file) is ".hlsl" or ".hlsli")
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(oldShaders, Is.Empty,
                "Legacy HLSL assets remain: " + string.Join(", ", oldShaders));
            foreach (string file in ShaderFiles())
            {
                string source = File.ReadAllText(file);
                string relative = Path.GetRelativePath(root, file);
                Assert.That(IncludeDirectiveRegex().IsMatch(source), Is.False,
                    $"{relative}: textual #include is forbidden; use import");
                Assert.That(source, Does.Not.Contain("[shader(\"pixel\")]"),
                    $"{relative}: use the canonical 'fragment' stage name");

            }
        });
    }

    [Test]
    public void EverySlangSourceBindsByParameterBlockOnly()
    {
        string root = RepoRoot();
        Assert.Multiple(() =>
        {
            foreach (string file in ShaderFiles())
            {
                string source = File.ReadAllText(file);
                string relative = Path.GetRelativePath(root, file);

                // The ParameterBlock contract: each resource group is one
                // annotation-free ParameterBlock<T> - the compiler owns both
                // the set (declaration order) and every binding number.
                // Register annotations (and vk::binding pins) would reintroduce
                // hand-maintained spaces the engine no longer reads.
                Assert.That(source, Does.Not.Contain("[[vk::binding"),
                    $"{relative}: ParameterBlock layout is compiler-owned; vk::binding is forbidden");
                Assert.That(RegisterRegex().IsMatch(source), Is.False,
                    $"{relative}: register() annotations are forbidden - group resources in a ParameterBlock<T> and let the compiler assign sets and bindings");

                // The preprocessor cannot carry qualified member names across a
                // ParameterBlock boundary (name##Sampler concatenation), and
                // the sampling helpers of AlcoRendering_Core replace the old
                // macro layer outright.
                Assert.That(SamplingMacroRegex().IsMatch(source), Is.False,
                    $"{relative}: SAMPLE_TEX*/GET_PIXEL*/LOAD_TEX* macros are retired; sample with qualified members or AlcoRendering_Core helpers");
            }
        });
    }

    [Test]
    public void FileNamesArePascalCaseAndModulesMatchStems()
    {
        Assert.Multiple(() =>
        {
            foreach ((string relative, string stem) in EngineShaderFiles())
            {
                string source = File.ReadAllText(Path.Combine(RepoRoot(), relative));

                // PascalCase, matching the C# identifiers of the same concept
                // (acronyms intact: FXAA, HBAO, SSR, VoxelGI, ImGUI). Library
                // modules carry an assembly prefix and exactly one underscore
                // (AlcoRendering_Core); pass/material modules are bare.
                Assert.That(stem, Does.Match("^[A-Z][A-Za-z0-9]*(_[A-Z][A-Za-z0-9]*)?$"),
                    $"{relative}: file name must be PascalCase (lib modules: Prefix_Concept, exactly one underscore)");

                Match module = ModuleNameRegex().Match(source);
                if (module.Success)
                {
                    // Engine and test modules pair stem and module exactly, so
                    // an import probe resolves to this file and this file only.
                    Assert.That(module.Groups[1].Value, Is.EqualTo(stem),
                        $"{relative}: module name must equal the file stem");
                }
            }
        });
    }

    [Test]
    public void ImportsResolveExactlyAgainstEngineModuleStems()
    {
        // The engine resolver answers import probes case-insensitively, so a
        // mistyped 'alcorendering_core' import would silently resolve on every
        // platform. Imports in the engine trees must match a real module stem
        // case-exactly - the typo then fails loudly at compile time.
        HashSet<string> stems = EngineShaderFiles().Select(pair => pair.Stem)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Multiple(() =>
        {
            foreach ((string relative, _) in EngineShaderFiles())
            {
                string source = File.ReadAllText(Path.Combine(RepoRoot(), relative));
                foreach (Match import in ImportRegex().Matches(source))
                {
                    string name = import.Groups[1].Value;

                    // Standard-library imports (not module files) resolve
                    // through the compiler's own search paths.
                    Assert.That(stems.Contains(name) || IsKnownNonFileModule(name), Is.True,
                        $"{relative}: import '{name}' does not case-match any engine module stem");
                }
            }
        });
    }

    private static bool IsKnownNonFileModule(string name) =>
        name is "glsl" or "hlsl" or "metal" or "cuda" or "cpp" or "spirv";

    [GeneratedRegex(@"(?m)^#language[ \t]+slang[ \t]+2025[ \t]*$")]
    private static partial Regex LanguageDirectiveRegex();

    [GeneratedRegex(@"(?m)^[ \t]*module[ \t]+[A-Za-z_][A-Za-z0-9_.]*[ \t]*;")]
    private static partial Regex ModuleDeclarationRegex();

    [GeneratedRegex(@"(?m)^[ \t]*module[ \t]+([A-Za-z0-9_]+)[ \t]*;")]
    private static partial Regex ModuleNameRegex();

    [GeneratedRegex(@"(?m)^[ \t]*#include\b")]
    private static partial Regex IncludeDirectiveRegex();

    [GeneratedRegex(@"\bregister[ \t]*\(([^)]*)\)")]
    private static partial Regex RegisterRegex();

    [GeneratedRegex(@"(?m)^[ \t]*import[ \t]+([A-Za-z0-9_]+)[ \t]*;")]
    private static partial Regex ImportRegex();

    [GeneratedRegex(@"^[ 	]*#define[ 	]+(SAMPLE_TEX2D|SAMPLE_TEX2D_LEVEL|SAMPLE_TEX3D_LEVEL|SAMPLE_TEX2D_DEPTH_CMP|GET_PIXEL_TEX2D|LOAD_TEX3D)")]
    private static partial Regex SamplingMacroRegex();
}
