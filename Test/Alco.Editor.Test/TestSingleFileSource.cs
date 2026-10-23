using Alco.Editor;
using NUnit.Framework;

namespace Alco.Editor.Test;

/// <summary>
/// Tests for <see cref="SingleFileSource"/>: serves exactly one file under a root,
/// nothing else.
/// </summary>
[TestFixture]
public sealed class TestSingleFileSource
{
    private string _tempRoot = string.Empty;

    /// <summary>Creates a temporary root directory.</summary>
    [SetUp]
    public void SetUp()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "alco-editor-test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    /// <summary>Deletes the temporary root directory.</summary>
    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    /// <summary>Tests that only the declared file is served.</summary>
    [Test]
    public void ServesOnlyTheDeclaredFile()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot, "Configs"));
        File.WriteAllText(Path.Combine(_tempRoot, "Configs", "game.json"), "{}");
        File.WriteAllText(Path.Combine(_tempRoot, "Configs", "sibling.json"), "{}");

        var source = new SingleFileSource(_tempRoot, "Configs/game.json");

        Assert.That(source.AllFileNames.ToArray(), Is.EqualTo(new[] { "Configs/game.json" }));
        Assert.Multiple(() =>
        {
            Assert.That(source.TryGetStream("Configs/game.json", out Stream? stream, out _), Is.True);
            stream!.Dispose();
            Assert.That(source.TryGetStream("Configs/sibling.json", out _, out _), Is.False);
            Assert.That(source.TryGetStream("game.json", out _, out _), Is.False);
        });
    }

    /// <summary>Tests that a missing file is not served.</summary>
    [Test]
    public void MissingFileServesNothing()
    {
        var source = new SingleFileSource(_tempRoot, "Configs/missing.json");

        Assert.That(source.AllFileNames.ToArray(), Is.Empty);
        Assert.That(source.TryGetStream("Configs/missing.json", out _, out _), Is.False);
    }

    /// <summary>Tests that backslash paths are normalized.</summary>
    [Test]
    public void NormalizesBackslashes()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot, "Configs"));
        File.WriteAllText(Path.Combine(_tempRoot, "Configs", "game.json"), "{}");

        var source = new SingleFileSource(_tempRoot, @"Configs\game.json");

        Assert.That(source.AllFileNames.ToArray(), Is.EqualTo(new[] { "Configs/game.json" }));
        Assert.That(source.TryGetStream(@"Configs\game.json", out Stream? stream, out _), Is.True);
        stream!.Dispose();
    }
}
