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

                foreach (Match register in RegisterRegex().Matches(source))
                {
                    Assert.That(register.Groups[1].Value, Does.Contain(','),
                        $"{relative}: register declarations must specify an explicit set/space");
                }
            }
        });
    }

    [Test]
    public void EverySlangSourceBindsBySetScopedBlocksOnly()
    {
        string root = RepoRoot();
        Assert.Multiple(() =>
        {
            foreach (string file in ShaderFiles())
            {
                string source = File.ReadAllText(file);
                string relative = Path.GetRelativePath(root, file);

                // The set-only contract: resources live in cbuffer blocks that
                // declare just their set (`register(b0, spaceN)`); binding
                // numbers are compiler-assigned. Explicit vk::binding pairs
                // pin every member and defeat the convention.
                Assert.That(source, Does.Not.Contain("[[vk::binding"),
                    $"{relative}: declare the set with a cbuffer block instead of vk::binding");

                foreach (Match register in RegisterRegex().Matches(source))
                {
                    string line = LineOf(source, register.Index);
                    Assert.That(line, Does.Match(@"\b(cbuffer|ConstantBuffer<)"),
                        $"{relative}: register() is only for set-scoped cbuffer/ConstantBuffer blocks");
                }
            }
        });
    }

    private static string LineOf(string source, int index)
    {
        int start = source.LastIndexOf('\n', index) + 1;
        int end = source.IndexOf('\n', index);
        return source[start..(end < 0 ? source.Length : end)];
    }

    [Test]
    public void FileNamesAreKebabCaseAndModulesMatchStems()
    {
        string root = RepoRoot();
        Assert.Multiple(() =>
        {
            foreach (string file in ShaderFiles())
            {
                string relative = Path.GetRelativePath(root, file);
                string stem = Path.GetFileNameWithoutExtension(file);
                Match module = ModuleNameRegex().Match(File.ReadAllText(file));

                // Lowercase kebab-case files survive case-sensitive asset
                // systems (Linux/Android targets) and mirror Slang's own
                // file-name rule.
                Assert.That(stem, Does.Match("^[a-z0-9]+(-[a-z0-9]+)*$"),
                    $"{relative}: file name must be lowercase kebab-case");

                if (module.Success)
                {
                    string moduleName = module.Groups[1].Value;

                    // Acronyms stay intact: 'fxaa', never 'f_x_a_a'.
                    Assert.That(moduleName, Does.Match("^[a-z0-9]+(_[a-z0-9]+)*$"),
                        $"{relative}: module name must be lowercase snake_case");

                    // Sandbox samples carry their own module identity (e.g.
                    // 'sandbox1_shader' inside 'shader.slang'); engine and test
                    // modules must pair stem and module exactly ('gaussian-blur-…'
                    // file, 'gaussian_blur_…' module) so import probes resolve.
                    if (!relative.StartsWith("Sandbox"))
                    {
                        Assert.That(moduleName, Is.EqualTo(stem.Replace('-', '_')),
                            $"{relative}: module name must be the file stem in snake_case");
                    }
                }
            }
        });
    }

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
}
