using System.Text.Json;
using Alco.Editor.Extensibility;
using NUnit.Framework;

namespace Alco.Editor.Test;

/// <summary>Tests window registration and persisted workspace state without an engine or GUI context.</summary>
[TestFixture]
public sealed class TestEditorWorkspaceLayout
{
    /// <summary>New layouts use the default ratios, regions and visibility with one selected open tab per region.</summary>
    [Test]
    public void Reconcile_NewLayoutUsesRegistrationDefaults()
    {
        WindowRegistry registry = CreateDefaultRegistry();
        var layout = new EditorWorkspaceLayout();

        Assert.Multiple(() =>
        {
            Assert.That(layout.Windows, Is.Empty);
            Assert.That(layout.RightWidthRatio, Is.EqualTo(EditorWorkspaceLayout.DefaultRightWidthRatio));
            Assert.That(layout.BottomHeightRatio, Is.EqualTo(EditorWorkspaceLayout.DefaultBottomHeightRatio));
            Assert.That(registry.Windows[0].IsOpenByDefault, Is.True, "The interface default must be open.");
        });

        layout.Reconcile(registry);

        AssertDefaultLayout(layout, registry);
    }

    /// <summary>Selecting a tab clears only the selection of its siblings.</summary>
    /// <param name="region">The region containing multiple registered tabs.</param>
    [Test]
    public void TrySelect_MultipleTabsHaveOneSelectedTab([Values] EditorDockRegion region)
    {
        WindowRegistry registry = CreateRegistry(
            new FakeEditorWindow { Id = "first", DefaultRegion = region },
            new FakeEditorWindow { Id = "second", DefaultRegion = region },
            new DefaultHiddenEditorWindow { Id = "hidden", DefaultRegion = region });
        EditorWorkspaceLayout layout = CreateLayout(registry);

        AssertValidLayout(layout, registry);
        foreach (string id in new[] { "second", "first" })
        {
            Assert.That(layout.TrySelect(id), Is.True);
            Assert.That(layout.Windows.Where(window => window.IsSelected).Select(window => window.Id),
                Is.EqualTo(new[] { id }));
            Assert.That(layout.Windows.Where(window => window.IsOpen).Select(window => window.Id),
                Is.EqualTo(new[] { "first", "second" }));
            AssertValidLayout(layout, registry);
        }
    }

    /// <summary>Moving an open or hidden window opens and selects it without losing other windows or their placements.</summary>
    /// <param name="id">The open or default-hidden window to move.</param>
    /// <param name="region">The destination workspace region.</param>
    [Test]
    public void TryMove_EachRegionPreservesOtherWindows(
        [Values("assets", "diagnostics")] string id,
        [Values] EditorDockRegion region)
    {
        WindowRegistry registry = CreateDefaultRegistry();
        EditorWorkspaceLayout layout = CreateLayout(registry);
        IEditorWindow[] registrations = registry.Windows.ToArray();
        var otherPlacements = layout.Windows.Where(window => window.Id != id)
            .Select(window => (window.Id, window.Region, window.IsOpen)).ToArray();

        Assert.That(layout.TryMove(id, region), Is.True);

        AssertPlacement(layout, id, region, isOpen: true, isSelected: true);
        Assert.Multiple(() =>
        {
            Assert.That(registry.Windows, Is.EqualTo(registrations));
            Assert.That(layout.Windows.Where(window => window.Id != id)
                .Select(window => (window.Id, window.Region, window.IsOpen)), Is.EqualTo(otherPlacements));
        });
        AssertValidLayout(layout, registry);
    }

    /// <summary>Hiding either selected tab selects the surviving open tab in the same region.</summary>
    /// <param name="hiddenId">The selected window to hide.</param>
    /// <param name="survivorId">The window that remains visible.</param>
    [TestCase("assets", "console")]
    [TestCase("console", "assets")]
    public void TrySetOpen_HidingSelectedTabSelectsSurvivor(string hiddenId, string survivorId)
    {
        WindowRegistry registry = CreateDefaultRegistry();
        EditorWorkspaceLayout layout = CreateLayout(registry);
        Assert.That(layout.TrySelect(hiddenId), Is.True);

        Assert.That(layout.TrySetOpen(hiddenId, false), Is.True);

        AssertPlacement(layout, hiddenId, EditorDockRegion.Bottom, isOpen: false, isSelected: false);
        AssertPlacement(layout, survivorId, EditorDockRegion.Bottom, isOpen: true, isSelected: true);
        AssertPlacement(layout, "document", EditorDockRegion.Top, isOpen: true, isSelected: true);
        AssertPlacement(layout, "inspector", EditorDockRegion.Right, isOpen: true, isSelected: true);
        AssertValidLayout(layout, registry);
    }

    /// <summary>Hiding the last open tab leaves its region with no selected tab.</summary>
    [Test]
    public void TrySetOpen_HidingLastOpenTabClearsSelection()
    {
        WindowRegistry registry = CreateDefaultRegistry();
        EditorWorkspaceLayout layout = CreateLayout(registry);

        Assert.That(layout.TrySetOpen("inspector", false), Is.True);

        AssertPlacement(layout, "inspector", EditorDockRegion.Right, isOpen: false, isSelected: false);
        AssertPlacement(layout, "diagnostics", EditorDockRegion.Right, isOpen: false, isSelected: false);
        AssertValidLayout(layout, registry);
    }

    /// <summary>Showing a hidden window opens and selects it, including windows hidden by default.</summary>
    /// <param name="id">The window to hide and show.</param>
    [TestCase("assets")]
    [TestCase("diagnostics")]
    public void TrySetOpen_ShowingWindowOpensAndSelectsIt(string id)
    {
        WindowRegistry registry = CreateDefaultRegistry();
        EditorWorkspaceLayout layout = CreateLayout(registry);
        EditorDockRegion region = GetWindow(layout, id).Region;
        Assert.That(layout.TrySetOpen(id, false), Is.True);
        AssertPlacement(layout, id, region, isOpen: false, isSelected: false);
        AssertValidLayout(layout, registry);

        Assert.That(layout.TrySetOpen(id, true), Is.True);

        AssertPlacement(layout, id, region, isOpen: true, isSelected: true);
        AssertValidLayout(layout, registry);
    }

    /// <summary>Hidden windows cannot be selected and a failed selection does not change persisted state.</summary>
    [Test]
    public void TrySelect_HiddenWindowReturnsFalseWithoutChangingState()
    {
        EditorWorkspaceLayout layout = CreateLayout(CreateDefaultRegistry());
        string before = JsonSerializer.Serialize(layout);

        Assert.That(layout.TrySelect("diagnostics"), Is.False);

        Assert.That(JsonSerializer.Serialize(layout), Is.EqualTo(before));
    }

    /// <summary>Reset restores ratios, placements, selections and default-hidden visibility.</summary>
    [Test]
    public void Reset_RestoresAllDefaults()
    {
        WindowRegistry registry = CreateDefaultRegistry();
        EditorWorkspaceLayout layout = CreateLayout(registry);
        string defaults = JsonSerializer.Serialize(layout);
        layout.RightWidthRatio = 0.6f;
        layout.BottomHeightRatio = 0.7f;
        Assert.That(layout.TryMove("document", EditorDockRegion.Bottom), Is.True);
        Assert.That(layout.TryMove("console", EditorDockRegion.Right), Is.True);
        Assert.That(layout.TrySetOpen("assets", false), Is.True);
        Assert.That(layout.TrySetOpen("diagnostics", true), Is.True);

        layout.Reset(registry);

        AssertDefaultLayout(layout, registry);
        Assert.That(JsonSerializer.Serialize(layout), Is.EqualTo(defaults));
    }

    /// <summary>Unknown IDs fail all lookup and mutation operations without changing state.</summary>
    /// <param name="id">An ID with no registered placement.</param>
    [TestCase("missing")]
    [TestCase("")]
    [TestCase(" \t")]
    public void TryOperations_UnknownIdReturnsFalseWithoutChangingState(string id)
    {
        EditorWorkspaceLayout layout = CreateLayout(CreateDefaultRegistry());
        string before = JsonSerializer.Serialize(layout);

        Assert.Multiple(() =>
        {
            Assert.That(layout.TryGetWindow(id, out EditorWindowPlacement missing), Is.False);
            Assert.That(missing, Is.Null);
            Assert.That(JsonSerializer.Serialize(layout), Is.EqualTo(before));
            Assert.That(layout.TryMove(id, EditorDockRegion.Top), Is.False);
            Assert.That(JsonSerializer.Serialize(layout), Is.EqualTo(before));
            Assert.That(layout.TrySetOpen(id, true), Is.False);
            Assert.That(JsonSerializer.Serialize(layout), Is.EqualTo(before));
            Assert.That(layout.TrySetOpen(id, false), Is.False);
            Assert.That(JsonSerializer.Serialize(layout), Is.EqualTo(before));
            Assert.That(layout.TrySelect(id), Is.False);
            Assert.That(JsonSerializer.Serialize(layout), Is.EqualTo(before));
        });
    }

    /// <summary>Invalid destination regions fail without opening, moving or selecting a window.</summary>
    /// <param name="region">An undefined enum value.</param>
    [TestCase(-1)]
    [TestCase(3)]
    public void TryMove_InvalidRegionReturnsFalseWithoutChangingState(int region)
    {
        EditorWorkspaceLayout layout = CreateLayout(CreateDefaultRegistry());
        string before = JsonSerializer.Serialize(layout);

        Assert.That(layout.TryMove("diagnostics", (EditorDockRegion)region), Is.False);

        Assert.That(JsonSerializer.Serialize(layout), Is.EqualTo(before));
    }

    /// <summary>JSON round-tripping preserves custom ratios and every placement field before and after reconciliation.</summary>
    [Test]
    public void JsonRoundTrip_PreservesWorkspaceState()
    {
        WindowRegistry registry = CreateDefaultRegistry();
        EditorWorkspaceLayout layout = CreateLayout(registry);
        layout.RightWidthRatio = 0.41f;
        layout.BottomHeightRatio = 0.63f;
        Assert.That(layout.TryMove("document", EditorDockRegion.Right), Is.True);
        Assert.That(layout.TrySetOpen("inspector", false), Is.True);
        Assert.That(layout.TrySetOpen("diagnostics", true), Is.True);
        Assert.That(layout.TrySelect("document"), Is.True);
        Assert.That(layout.TrySelect("console"), Is.True);

        string json = JsonSerializer.Serialize(layout);
        EditorWorkspaceLayout restored = JsonSerializer.Deserialize<EditorWorkspaceLayout>(json)!;

        Assert.That(JsonSerializer.Serialize(restored), Is.EqualTo(json));
        AssertValidLayout(restored, registry);
        restored.Reconcile(registry);
        Assert.That(JsonSerializer.Serialize(restored), Is.EqualTo(json));
        AssertValidLayout(restored, registry);
    }

    /// <summary>Reconciliation preserves known saved state, adds new defaults and removes obsolete registrations.</summary>
    [Test]
    public void Reconcile_ChangedRegistrationsPreserveKnownPlacements()
    {
        WindowRegistry registry = CreateRegistry(
            new FakeEditorWindow { Id = "new", DefaultRegion = EditorDockRegion.Right },
            new FakeEditorWindow { Id = "existing", DefaultRegion = EditorDockRegion.Bottom },
            new FakeEditorWindow { Id = "hidden", DefaultRegion = EditorDockRegion.Bottom },
            new DefaultHiddenEditorWindow { Id = "new-hidden", DefaultRegion = EditorDockRegion.Top });
        var layout = new EditorWorkspaceLayout
        {
            RightWidthRatio = 0.42f,
            BottomHeightRatio = 0.56f,
            Windows =
            [
                new() { Id = "existing", Region = EditorDockRegion.Right, IsOpen = true, IsSelected = true },
                new() { Id = "hidden", Region = EditorDockRegion.Top, IsOpen = false },
                new() { Id = "obsolete", Region = EditorDockRegion.Bottom, IsOpen = true, IsSelected = true },
            ],
        };

        layout.Reconcile(registry);

        AssertPlacement(layout, "existing", EditorDockRegion.Right, isOpen: true, isSelected: true);
        AssertPlacement(layout, "hidden", EditorDockRegion.Top, isOpen: false, isSelected: false);
        AssertPlacement(layout, "new", EditorDockRegion.Right, isOpen: true, isSelected: false);
        AssertPlacement(layout, "new-hidden", EditorDockRegion.Top, isOpen: false, isSelected: false);
        Assert.Multiple(() =>
        {
            Assert.That(layout.RightWidthRatio, Is.EqualTo(0.42f));
            Assert.That(layout.BottomHeightRatio, Is.EqualTo(0.56f));
            Assert.That(layout.TryGetWindow("obsolete", out _), Is.False);
        });
        AssertValidLayout(layout, registry);
        string reconciled = JsonSerializer.Serialize(layout);
        layout.Reconcile(registry);
        Assert.That(JsonSerializer.Serialize(layout), Is.EqualTo(reconciled), "Reconciliation must be idempotent.");
    }

    /// <summary>Finite ratios are clamped to the supported range while in-range ratios are preserved.</summary>
    /// <param name="ratio">The saved width and height ratio.</param>
    /// <param name="expected">The normalized ratio.</param>
    [TestCase(-1f, 0.1f)]
    [TestCase(0f, 0.1f)]
    [TestCase(0.1f, 0.1f)]
    [TestCase(0.45f, 0.45f)]
    [TestCase(0.8f, 0.8f)]
    [TestCase(1f, 0.8f)]
    public void Reconcile_FiniteRatiosAreClamped(float ratio, float expected)
    {
        var layout = new EditorWorkspaceLayout { RightWidthRatio = ratio, BottomHeightRatio = ratio };

        layout.Reconcile(new WindowRegistry());

        Assert.Multiple(() =>
        {
            Assert.That(layout.RightWidthRatio, Is.EqualTo(expected));
            Assert.That(layout.BottomHeightRatio, Is.EqualTo(expected));
            Assert.That(layout.Windows, Is.Empty);
        });
    }

    /// <summary>Nonfinite ratios fall back to their respective defaults rather than reaching persisted UI geometry.</summary>
    /// <param name="ratio">A nonfinite saved ratio.</param>
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(float.NegativeInfinity)]
    public void Reconcile_NonfiniteRatiosUseDefaults(float ratio)
    {
        var layout = new EditorWorkspaceLayout { RightWidthRatio = ratio, BottomHeightRatio = ratio };

        layout.Reconcile(new WindowRegistry());

        Assert.Multiple(() =>
        {
            Assert.That(layout.RightWidthRatio, Is.EqualTo(EditorWorkspaceLayout.DefaultRightWidthRatio));
            Assert.That(layout.BottomHeightRatio, Is.EqualTo(EditorWorkspaceLayout.DefaultBottomHeightRatio));
        });
    }

    /// <summary>Missing or null saved collections and null entries are replaced with registration defaults.</summary>
    /// <param name="json">The incomplete or malformed saved layout.</param>
    [TestCase("{}")]
    [TestCase("{\"Windows\":null}")]
    [TestCase("{\"Windows\":[null,null]}")]
    public void Reconcile_NullSavedWindowsRestoreDefaults(string json)
    {
        WindowRegistry registry = CreateDefaultRegistry();
        EditorWorkspaceLayout layout = JsonSerializer.Deserialize<EditorWorkspaceLayout>(json)!;

        layout.Reconcile(registry);

        AssertDefaultLayout(layout, registry);
    }

    /// <summary>Duplicate entries keep the first known placement and invalid or null entries cannot introduce windows.</summary>
    [Test]
    public void Reconcile_DuplicateAndNullEntriesNormalizeToOnePlacementPerRegistration()
    {
        WindowRegistry registry = CreateDefaultRegistry();
        var layout = new EditorWorkspaceLayout
        {
            Windows =
            [
                null!,
                new() { Id = "assets", Region = EditorDockRegion.Right, IsOpen = false, IsSelected = true },
                new() { Id = "assets", Region = EditorDockRegion.Top, IsOpen = true, IsSelected = true },
                new() { Id = "inspector", Region = EditorDockRegion.Right, IsOpen = true, IsSelected = true },
                new() { Id = string.Empty },
                new() { Id = null! },
                null!,
            ],
        };

        layout.Reconcile(registry);

        AssertPlacement(layout, "assets", EditorDockRegion.Right, isOpen: false, isSelected: false);
        AssertPlacement(layout, "inspector", EditorDockRegion.Right, isOpen: true, isSelected: true);
        AssertPlacement(layout, "console", EditorDockRegion.Bottom, isOpen: true, isSelected: true);
        AssertValidLayout(layout, registry);
    }

    /// <summary>Invalid saved regions fall back to registration defaults without discarding saved visibility.</summary>
    /// <param name="region">An undefined saved enum value.</param>
    [TestCase(-1)]
    [TestCase(3)]
    public void Reconcile_InvalidSavedRegionsUseDefaults(int region)
    {
        WindowRegistry registry = CreateDefaultRegistry();
        var layout = new EditorWorkspaceLayout
        {
            Windows =
            [
                new() { Id = "assets", Region = (EditorDockRegion)region, IsOpen = false, IsSelected = true },
                new() { Id = "diagnostics", Region = (EditorDockRegion)region, IsOpen = true, IsSelected = true },
            ],
        };

        layout.Reconcile(registry);

        AssertPlacement(layout, "assets", EditorDockRegion.Bottom, isOpen: false, isSelected: false);
        AssertPlacement(layout, "diagnostics", EditorDockRegion.Right, isOpen: true, isSelected: true);
        AssertValidLayout(layout, registry);
    }

    /// <summary>Saved multiple selections and selected hidden windows normalize to one open selection per region.</summary>
    [Test]
    public void Reconcile_InvalidSelectionsAreNormalized()
    {
        WindowRegistry registry = CreateDefaultRegistry();
        EditorWorkspaceLayout layout = CreateLayout(registry);
        foreach (EditorWindowPlacement window in layout.Windows)
        {
            window.IsSelected = true;
        }

        layout.Reconcile(registry);

        AssertPlacement(layout, "assets", EditorDockRegion.Bottom, isOpen: true, isSelected: true);
        AssertPlacement(layout, "console", EditorDockRegion.Bottom, isOpen: true, isSelected: false);
        AssertPlacement(layout, "diagnostics", EditorDockRegion.Right, isOpen: false, isSelected: false);
        AssertValidLayout(layout, registry);
    }

    /// <summary>Registration uses stable IDs rather than titles and retains the original instances in order.</summary>
    [Test]
    public void Register_DistinctIdsMayShareTitles()
    {
        var first = new FakeEditorWindow { Id = "first", Title = "Shared title" };
        var second = new FakeEditorWindow { Id = "second", Title = "Shared title" };

        WindowRegistry registry = CreateRegistry(first, second);

        Assert.That(registry.Windows, Is.EqualTo(new IEditorWindow[] { first, second }));
    }

    /// <summary>Registering a null window is a programmer error and cannot change existing registrations.</summary>
    [Test]
    public void Register_NullWindowThrowsWithoutChangingRegistry()
    {
        WindowRegistry registry = CreateDefaultRegistry();
        IEditorWindow[] before = registry.Windows.ToArray();

        Assert.Throws<ArgumentNullException>(() => registry.Register(null!));

        Assert.That(registry.Windows, Is.EqualTo(before));
    }

    /// <summary>Null, empty or whitespace IDs are programmer errors and cannot change existing registrations.</summary>
    /// <param name="id">An invalid registration ID.</param>
    [TestCase(null)]
    [TestCase("")]
    [TestCase(" \t\r\n")]
    public void Register_InvalidIdThrowsWithoutChangingRegistry(string? id)
    {
        WindowRegistry registry = CreateDefaultRegistry();
        IEditorWindow[] before = registry.Windows.ToArray();
        var window = new FakeEditorWindow { Id = id! };

        Assert.That(() => registry.Register(window), Throws.InstanceOf<ArgumentException>());

        Assert.That(registry.Windows, Is.EqualTo(before));
    }

    /// <summary>A second instance with an existing ID is rejected even when its title and region differ.</summary>
    [Test]
    public void Register_DuplicateIdThrowsWithoutChangingRegistry()
    {
        WindowRegistry registry = CreateDefaultRegistry();
        IEditorWindow[] before = registry.Windows.ToArray();
        var duplicate = new FakeEditorWindow
        {
            Id = "document",
            Title = "Another title",
            DefaultRegion = EditorDockRegion.Right,
        };

        Assert.Throws<ArgumentException>(() => registry.Register(duplicate));

        Assert.That(registry.Windows, Is.EqualTo(before));
    }

    /// <summary>Invalid default regions are programmer errors and cannot change existing registrations.</summary>
    /// <param name="region">An undefined default region.</param>
    [TestCase(-1)]
    [TestCase(3)]
    public void Register_InvalidRegionThrowsWithoutChangingRegistry(int region)
    {
        WindowRegistry registry = CreateDefaultRegistry();
        IEditorWindow[] before = registry.Windows.ToArray();
        var window = new FakeEditorWindow { Id = "invalid-region", DefaultRegion = (EditorDockRegion)region };

        Assert.Throws<ArgumentOutOfRangeException>(() => registry.Register(window));

        Assert.That(registry.Windows, Is.EqualTo(before));
    }

    private static WindowRegistry CreateDefaultRegistry() => CreateRegistry(
        new FakeEditorWindow { Id = "document", DefaultRegion = EditorDockRegion.Top },
        new FakeEditorWindow { Id = "inspector", DefaultRegion = EditorDockRegion.Right },
        new FakeEditorWindow { Id = "assets", DefaultRegion = EditorDockRegion.Bottom },
        new FakeEditorWindow { Id = "console", DefaultRegion = EditorDockRegion.Bottom },
        new DefaultHiddenEditorWindow { Id = "diagnostics", DefaultRegion = EditorDockRegion.Right });

    private static WindowRegistry CreateRegistry(params IEditorWindow[] windows)
    {
        var registry = new WindowRegistry();
        foreach (IEditorWindow window in windows)
        {
            registry.Register(window);
        }
        return registry;
    }

    private static EditorWorkspaceLayout CreateLayout(WindowRegistry registry)
    {
        var layout = new EditorWorkspaceLayout();
        layout.Reconcile(registry);
        return layout;
    }

    private static EditorWindowPlacement GetWindow(EditorWorkspaceLayout layout, string id)
    {
        Assert.That(layout.TryGetWindow(id, out EditorWindowPlacement window), Is.True, id);
        return window;
    }

    private static void AssertPlacement(EditorWorkspaceLayout layout, string id, EditorDockRegion region,
        bool isOpen, bool isSelected)
    {
        EditorWindowPlacement window = GetWindow(layout, id);
        Assert.Multiple(() =>
        {
            Assert.That(window.Id, Is.EqualTo(id));
            Assert.That(window.Region, Is.EqualTo(region), id);
            Assert.That(window.IsOpen, Is.EqualTo(isOpen), id);
            Assert.That(window.IsSelected, Is.EqualTo(isSelected), id);
        });
    }

    private static void AssertDefaultLayout(EditorWorkspaceLayout layout, WindowRegistry registry)
    {
        Assert.Multiple(() =>
        {
            Assert.That(layout.RightWidthRatio, Is.EqualTo(EditorWorkspaceLayout.DefaultRightWidthRatio));
            Assert.That(layout.BottomHeightRatio, Is.EqualTo(EditorWorkspaceLayout.DefaultBottomHeightRatio));
            Assert.That(layout.Windows.Select(window => (window.Id, window.Region, window.IsOpen)),
                Is.EqualTo(registry.Windows.Select(window => (window.Id, window.DefaultRegion, window.IsOpenByDefault))));
        });
        AssertValidLayout(layout, registry);
    }

    private static void AssertValidLayout(EditorWorkspaceLayout layout, WindowRegistry registry)
    {
        Assert.Multiple(() =>
        {
            Assert.That(layout.Windows.Select(window => window.Id),
                Is.EqualTo(registry.Windows.Select(window => window.Id)), "Each registration must have exactly one placement.");
            Assert.That(layout.Windows.All(window => Enum.IsDefined(window.Region)), Is.True);
            Assert.That(layout.Windows.Any(window => !window.IsOpen && window.IsSelected), Is.False);
            foreach (EditorDockRegion region in Enum.GetValues<EditorDockRegion>())
            {
                bool hasOpenWindow = layout.Windows.Any(window => window.Region == region && window.IsOpen);
                int selectedCount = layout.Windows.Count(window => window.Region == region && window.IsSelected);
                Assert.That(selectedCount, Is.EqualTo(hasOpenWindow ? 1 : 0), region.ToString());
            }
        });
    }

    private class FakeEditorWindow : IEditorWindow
    {
        /// <summary>The stable ID used by the registry and layout.</summary>
        public string Id { get; init; } = "window";

        /// <summary>The display title, which does not determine window identity.</summary>
        public string Title { get; init; } = "Window";

        /// <summary>The region used by a fresh or reset layout.</summary>
        public EditorDockRegion DefaultRegion { get; init; } = EditorDockRegion.Top;

        /// <summary>Does nothing and requires no engine or GUI context.</summary>
        /// <param name="context">The unused editor context.</param>
        public void Draw(EditorContext context)
        {
        }
    }

    private sealed class DefaultHiddenEditorWindow : FakeEditorWindow, IEditorWindow
    {
        /// <summary>Overrides the interface default so this window starts hidden.</summary>
        public bool IsOpenByDefault => false;
    }
}
