using Alco.Editor;
using Alco.IO;
using NUnit.Framework;

namespace Alco.Editor.Test;

/// <summary>
/// Tests for <see cref="ProjectAssetMount"/> mount/unmount symmetry — the mechanism
/// behind runtime project switching. Each test works in its own temporary directory
/// tree with a standalone asset system (no engine).
/// </summary>
[TestFixture]
public sealed class TestProjectAssetMount
{
    private string _tempRoot = string.Empty;
    private AssetSystem _assetSystem = null!;

    [SetUp]
    public void SetUp()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "alco-editor-test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        _assetSystem = new AssetSystem(new SilentHost());
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    /// <summary>An asset system host that swallows logging and runs posted work inline.</summary>
    private sealed class SilentHost : IAssetSystemHost
    {
        public event Action OnDispose
        {
            add { }
            remove { }
        }

        public void PostToMainThread(Action action) => action();

        public void LogInfo(ReadOnlySpan<char> message) { }

        public void LogWarning(ReadOnlySpan<char> message) { }

        public void LogError(ReadOnlySpan<char> message) { }

        public void LogSuccess(ReadOnlySpan<char> message) { }
    }

    private AlcoProject WriteProject(string fileName, string ownedDirectory)
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot, ownedDirectory));
        File.WriteAllText(Path.Combine(_tempRoot, ownedDirectory, ownedDirectory.ToLowerInvariant() + ".txt"), "asset");
        string path = Path.Combine(_tempRoot, fileName);
        File.WriteAllText(path, $$"""{ "assetsPaths": [ "{{ownedDirectory}}" ] }""");
        return AlcoProject.Load(path);
    }

    /// <summary>
    /// Forces the entry rescan that the editor triggers through the asset browser's
    /// AllAssetNames read — <see cref="AssetSystem.IsFileExist"/> does not refresh
    /// entries by itself.
    /// </summary>
    private void RefreshEntries() => _assetSystem.ForceRefreshEntries();

    [Test]
    public void Mount_ExposesOwnedAssets()
    {
        AlcoProject project = WriteProject("ProjectA.alco", "AssetsA");

        IReadOnlyList<IFileSource> mounted = ProjectAssetMount.Mount(project, _assetSystem);
        RefreshEntries();

        Assert.Multiple(() =>
        {
            Assert.That(mounted, Is.Not.Empty);
            Assert.That(_assetSystem.IsFileExist("assetsa.txt"), Is.True);
        });
    }

    [Test]
    public void Unmount_RemovesMountedSources()
    {
        AlcoProject project = WriteProject("ProjectA.alco", "AssetsA");
        IReadOnlyList<IFileSource> mounted = ProjectAssetMount.Mount(project, _assetSystem);
        RefreshEntries();
        Assert.That(_assetSystem.IsFileExist("assetsa.txt"), Is.True);

        ProjectAssetMount.Unmount(mounted, _assetSystem);
        RefreshEntries();

        Assert.That(_assetSystem.IsFileExist("assetsa.txt"), Is.False);
    }

    [Test]
    public void UnmountThenMount_SwitchesToAnotherProject()
    {
        AlcoProject projectA = WriteProject("ProjectA.alco", "AssetsA");
        AlcoProject projectB = WriteProject("ProjectB.alco", "AssetsB");

        IReadOnlyList<IFileSource> mounted = ProjectAssetMount.Mount(projectA, _assetSystem);
        RefreshEntries();
        Assert.That(_assetSystem.IsFileExist("assetsa.txt"), Is.True);
        Assert.That(_assetSystem.IsFileExist("assetsb.txt"), Is.False);

        ProjectAssetMount.Unmount(mounted, _assetSystem);
        mounted = ProjectAssetMount.Mount(projectB, _assetSystem);
        RefreshEntries();

        Assert.Multiple(() =>
        {
            Assert.That(_assetSystem.IsFileExist("assetsb.txt"), Is.True);
            Assert.That(_assetSystem.IsFileExist("assetsa.txt"), Is.False);
        });
    }

    [Test]
    public void Unmount_ReleasesWatchedDirectories()
    {
        // Windows cannot delete a directory tree while a watcher holds it open, so a
        // clean unmount is required before the project directory becomes removable.
        AlcoProject project = WriteProject("ProjectA.alco", "AssetsA");
        IReadOnlyList<IFileSource> mounted = ProjectAssetMount.Mount(project, _assetSystem);

        ProjectAssetMount.Unmount(mounted, _assetSystem);

        string owned = Path.Combine(_tempRoot, "AssetsA");
        Assert.DoesNotThrow(() => Directory.Delete(owned, recursive: true));
        Assert.That(Directory.Exists(owned), Is.False);
    }
}
