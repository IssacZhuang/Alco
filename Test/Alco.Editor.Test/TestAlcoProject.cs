using Alco.Editor;
using NUnit.Framework;

namespace Alco.Editor.Test;

/// <summary>
/// Tests for <see cref="AlcoProject"/> parsing, path resolution and ownership checks.
/// Each test works in its own temporary directory tree.
/// </summary>
[TestFixture]
public sealed class TestAlcoProject
{
    private string _tempRoot = string.Empty;

    /// <summary>Creates a fresh temporary directory for each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "alco-editor-test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    /// <summary>Deletes the test's temporary directory.</summary>
    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private string WriteProjectFile(string content, string fileName = "Game.alco")
    {
        string path = Path.Combine(_tempRoot, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>Tests that project loading accepts PascalCase and camelCase keys.</summary>
    [Test]
    public void Load_AcceptsPascalCaseAndCamelCase()
    {
        string pascalPath = WriteProjectFile("""
            { "Name": "Pascal", "AssetsPaths": [ "Assets" ], "ReferencedAssets": [ "Engine/Assets" ] }
            """, "Pascal.alco");
        string camelPath = WriteProjectFile("""
            { "name": "Camel", "assetsPaths": [ "Assets" ], "referencedAssets": [ "Engine/Assets" ] }
            """, "Camel.alco");

        AlcoProject pascal = AlcoProject.Load(pascalPath);
        AlcoProject camel = AlcoProject.Load(camelPath);

        Assert.Multiple(() =>
        {
            Assert.That(pascal.Name, Is.EqualTo("Pascal"));
            Assert.That(pascal.AssetsPaths, Is.EqualTo(new[] { "Assets" }));
            Assert.That(pascal.ReferencedAssets, Is.EqualTo(new[] { "Engine/Assets" }));
            Assert.That(camel.Name, Is.EqualTo("Camel"));
            Assert.That(camel.AssetsPaths, Is.EqualTo(new[] { "Assets" }));
            Assert.That(camel.ReferencedAssets, Is.EqualTo(new[] { "Engine/Assets" }));
        });
    }

    /// <summary>Tests that a single string is accepted for path lists.</summary>
    [Test]
    public void Load_AcceptsSingleStringForPathLists()
    {
        string path = WriteProjectFile("""{ "assetsPaths": "Assets" }""");

        AlcoProject project = AlcoProject.Load(path);

        Assert.That(project.AssetsPaths, Is.EqualTo(new[] { "Assets" }));
    }

    /// <summary>Tests that the project name defaults to the file name.</summary>
    [Test]
    public void Load_DefaultsNameToFileName()
    {
        string path = WriteProjectFile("""{ "assetsPaths": [] }""", "MyGame.alco");

        AlcoProject project = AlcoProject.Load(path);

        Assert.That(project.Name, Is.EqualTo("MyGame"));
    }

    /// <summary>Tests that loading a non-.alco file is rejected.</summary>
    [Test]
    public void Load_RejectsNonAlcoFiles()
    {
        string path = WriteProjectFile("{}", "Game.slnx");

        Assert.Throws<InvalidDataException>(() => AlcoProject.Load(path));
    }

    /// <summary>Tests that loading a missing file is rejected.</summary>
    [Test]
    public void Load_RejectsMissingFiles()
    {
        Assert.Throws<FileNotFoundException>(() => AlcoProject.Load(Path.Combine(_tempRoot, "Nope.alco")));
    }

    /// <summary>Tests that relative asset paths resolve against the project directory.</summary>
    [Test]
    public void Paths_ResolveAgainstProjectDirectory()
    {
        string path = WriteProjectFile("""{ "assetsPaths": [ "Assets" ] }""");

        AlcoProject project = AlcoProject.Load(path);

        string expected = Path.GetFullPath(Path.Combine(_tempRoot, "Assets")).Replace('\\', '/');
        Assert.Multiple(() =>
        {
            Assert.That(project.ProjectDirectory.Replace('\\', '/'), Is.EqualTo(_tempRoot.Replace('\\', '/')));
            Assert.That(project.GetAbsoluteAssetRoots(), Is.EqualTo(new[] { expected }));
        });
    }

    /// <summary>Tests that saving writes camelCase keys and round-trips the project.</summary>
    [Test]
    public void Save_WritesCamelCaseAndRoundTrips()
    {
        var project = AlcoProject.CreateUntitled(_tempRoot);
        project.Name = "Round Trip";
        project.AssetsPaths = new List<string> { "Assets", "Configs" };
        project.ReferencedAssets = new List<string> { "Engine/Assets" };

        string path = Path.Combine(_tempRoot, "RoundTrip.alco");
        project.Save(path);
        string json = File.ReadAllText(path);

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("\"name\""));
            Assert.That(json, Does.Contain("\"assetsPaths\""));
            Assert.That(json, Does.Not.Contain("\"AssetsPaths\""));
        });

        AlcoProject loaded = AlcoProject.Load(path);
        Assert.Multiple(() =>
        {
            Assert.That(loaded.Name, Is.EqualTo("Round Trip"));
            Assert.That(loaded.AssetsPaths, Is.EqualTo(project.AssetsPaths));
            Assert.That(loaded.ReferencedAssets, Is.EqualTo(project.ReferencedAssets));
            Assert.That(loaded.IsUntitled, Is.False);
        });
    }

    /// <summary>Tests that an untitled project uses the given directory as its asset root.</summary>
    [Test]
    public void CreateUntitled_RootsItselfAtTheGivenDirectory()
    {
        AlcoProject project = AlcoProject.CreateUntitled(_tempRoot);

        Assert.Multiple(() =>
        {
            Assert.That(project.IsUntitled, Is.True);
            Assert.That(project.GetAbsoluteAssetRoots(), Is.EqualTo(new[] { _tempRoot.Replace('\\', '/') }));
        });
    }

    /// <summary>Tests that owned and referenced asset paths resolve to their absolute files.</summary>
    [Test]
    public void Ownership_OwnedAndReferencedFilesResolve()
    {
        // Layout: <root>/Assets/foo.png (owned), <root>/Engine/Assets/bar.png (referenced dir),
        // <root>/Configs/game.json (referenced single file)
        Directory.CreateDirectory(Path.Combine(_tempRoot, "Assets"));
        Directory.CreateDirectory(Path.Combine(_tempRoot, "Engine", "Assets"));
        Directory.CreateDirectory(Path.Combine(_tempRoot, "Configs"));
        File.WriteAllText(Path.Combine(_tempRoot, "Assets", "foo.png"), "a");
        File.WriteAllText(Path.Combine(_tempRoot, "Engine", "Assets", "bar.png"), "b");
        File.WriteAllText(Path.Combine(_tempRoot, "Configs", "game.json"), "{}");

        string path = WriteProjectFile("""
            {
                "assetsPaths": [ "Assets" ],
                "referencedAssets": [ "Engine/Assets", "Configs/game.json" ]
            }
            """);

        AlcoProject project = AlcoProject.Load(path);

        Assert.Multiple(() =>
        {
            Assert.That(project.IsOwnedAsset("foo.png"), Is.True);
            Assert.That(project.IsOwnedAsset("bar.png"), Is.False);
            Assert.That(project.IsOwnedAsset("missing.png"), Is.False);

            Assert.That(project.TryGetOwnedAbsolutePath("foo.png", out string? ownedFoo), Is.True);
            Assert.That(ownedFoo.Replace('\\', '/'), Does.EndWith("/Assets/foo.png"));

            Assert.That(project.TryGetReferencedAbsolutePath("bar.png", out string? refBar), Is.True);
            Assert.That(refBar.Replace('\\', '/'), Does.EndWith("/Engine/Assets/bar.png"));

            Assert.That(project.TryGetReferencedAbsolutePath("Configs/game.json", out string? refConfig), Is.True);
            Assert.That(refConfig.Replace('\\', '/'), Does.EndWith("/Configs/game.json"));

            Assert.That(project.TryGetReferencedAbsolutePath("nope.png", out _), Is.False);
        });
    }

    /// <summary>Tests that an owned asset shadows a referenced asset with the same path.</summary>
    [Test]
    public void Ownership_OwnedShadowsReferenced()
    {
        // The same relative path exists in both; the owned root must win for edits.
        Directory.CreateDirectory(Path.Combine(_tempRoot, "Assets"));
        Directory.CreateDirectory(Path.Combine(_tempRoot, "Engine", "Assets"));
        File.WriteAllText(Path.Combine(_tempRoot, "Assets", "shared.png"), "owned");
        File.WriteAllText(Path.Combine(_tempRoot, "Engine", "Assets", "shared.png"), "referenced");

        string path = WriteProjectFile("""
            { "assetsPaths": [ "Assets" ], "referencedAssets": [ "Engine/Assets" ] }
            """);

        AlcoProject project = AlcoProject.Load(path);

        Assert.That(project.IsOwnedAsset("shared.png"), Is.True);
    }
}
