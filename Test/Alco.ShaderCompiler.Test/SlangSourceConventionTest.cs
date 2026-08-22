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

    [GeneratedRegex(@"(?m)^#language[ \t]+slang[ \t]+2025[ \t]*$")]
    private static partial Regex LanguageDirectiveRegex();

    [GeneratedRegex(@"(?m)^[ \t]*module[ \t]+[A-Za-z_][A-Za-z0-9_.]*[ \t]*;")]
    private static partial Regex ModuleDeclarationRegex();

    [GeneratedRegex(@"(?m)^[ \t]*#include\b")]
    private static partial Regex IncludeDirectiveRegex();

    [GeneratedRegex(@"\bregister[ \t]*\(([^)]*)\)")]
    private static partial Regex RegisterRegex();
}
