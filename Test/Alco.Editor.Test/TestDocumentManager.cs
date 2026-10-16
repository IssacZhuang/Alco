using Alco.Editor.Extensibility;
using NUnit.Framework;

namespace Alco.Editor.Test;

/// <summary>Tests document ownership, activation and close notifications without an engine or GUI context.</summary>
[TestFixture]
public sealed class TestDocumentManager
{
    private const string FirstAssetPath = "first.testdoc";
    private const string SecondAssetPath = "second.testdoc";
    private const string ThirdAssetPath = "third.testdoc";

    private readonly List<FakeDocument> _createdDocuments = new();
    private string _tempRoot = string.Empty;

    /// <summary>Creates isolated owned assets for the document constructor's ownership check.</summary>
    [SetUp]
    public void SetUp()
    {
        _createdDocuments.Clear();
        _tempRoot = Path.Combine(Path.GetTempPath(), "alco-editor-test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        foreach (string assetPath in new[] { FirstAssetPath, SecondAssetPath, ThirdAssetPath })
        {
            File.WriteAllText(Path.Combine(_tempRoot, assetPath), string.Empty);
        }
    }

    /// <summary>Disposes remaining fake documents without invoking test observers and removes the temporary assets.</summary>
    [TearDown]
    public void TearDown()
    {
        foreach (FakeDocument document in _createdDocuments)
        {
            document.OnDisposing = null;
            document.Dispose();
        }
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    /// <summary>Every new document becomes active synchronously, before its opened and activated notifications.</summary>
    [Test]
    public void Open_NewDocumentIsActiveBeforeNotifications()
    {
        DocumentManager manager = CreateManager();
        var notifications = new List<(string Event, AssetDocument Document, AssetDocument? Active, bool IsListed)>();
        manager.DocumentOpened += document => notifications.Add(
            ("opened", document, manager.ActiveDocument, manager.Documents.Contains(document)));
        manager.DocumentActivated += document => notifications.Add(
            ("activated", document, manager.ActiveDocument, manager.Documents.Contains(document)));
        Assert.Multiple(() =>
        {
            Assert.That(manager.Documents, Is.Empty);
            Assert.That(manager.ActiveDocument, Is.Null);
        });

        manager.Open(FirstAssetPath);

        FakeDocument first = _createdDocuments[0];
        Assert.Multiple(() =>
        {
            Assert.That(manager.ActiveDocument, Is.SameAs(first));
            Assert.That(manager.FindOpen(FirstAssetPath), Is.SameAs(first));
            Assert.That(manager.Documents, Is.EqualTo(new[] { first }));
            Assert.That(first.IsReadOnly, Is.False);
        });

        manager.Open(SecondAssetPath);

        FakeDocument second = _createdDocuments[1];
        Assert.Multiple(() =>
        {
            Assert.That(manager.ActiveDocument, Is.SameAs(second));
            Assert.That(manager.FindOpen(SecondAssetPath), Is.SameAs(second));
            Assert.That(manager.Documents, Is.EqualTo(new[] { first, second }));
            Assert.That(notifications.Select(item => item.Event),
                Is.EqualTo(new[] { "opened", "activated", "opened", "activated" }));
            Assert.That(notifications.Select(item => item.Document), Is.EqualTo(new[] { first, first, second, second }));
            Assert.That(notifications.All(item => ReferenceEquals(item.Active, item.Document)), Is.True);
            Assert.That(notifications.All(item => item.IsListed), Is.True);
        });
    }

    /// <summary>Opening an existing document selects its original instance and raises only activation, even when already active.</summary>
    /// <param name="assetPath">The existing asset path, optionally with different casing.</param>
    [TestCase(FirstAssetPath)]
    [TestCase("FIRST.TESTDOC")]
    public void Open_ExistingDocumentIsActivatedWithoutAnotherOpenEvent(string assetPath)
    {
        DocumentManager manager = CreateManager();
        manager.Open(FirstAssetPath);
        manager.Open(SecondAssetPath);
        FakeDocument first = _createdDocuments[0];
        FakeDocument second = _createdDocuments[1];
        first.Modify();
        var opened = new List<AssetDocument>();
        var activated = new List<(AssetDocument Document, AssetDocument? Active)>();
        manager.DocumentOpened += opened.Add;
        manager.DocumentActivated += document => activated.Add((document, manager.ActiveDocument));

        manager.Open(assetPath);

        Assert.That(manager.ActiveDocument, Is.SameAs(first));
        manager.Open(assetPath);

        Assert.Multiple(() =>
        {
            Assert.That(manager.ActiveDocument, Is.SameAs(first));
            Assert.That(manager.FindOpen(assetPath), Is.SameAs(first));
            Assert.That(manager.Documents, Is.EqualTo(new[] { first, second }));
            Assert.That(_createdDocuments, Has.Count.EqualTo(2));
            Assert.That(opened, Is.Empty);
            Assert.That(activated.Select(item => item.Document), Is.EqualTo(new[] { first, first }));
            Assert.That(activated.All(item => ReferenceEquals(item.Active, first)), Is.True);
            Assert.That(first.IsDirty, Is.True);
            Assert.That(first.IsDisposed, Is.False);
        });
    }

    /// <summary>Closing an active document publishes the adjacent survivor or null before disposal and the closed notification.</summary>
    /// <param name="documentCount">The number of documents to open.</param>
    /// <param name="selectedIndex">The index of the document to select and close.</param>
    /// <param name="survivorIndex">The original index of the expected survivor, or minus one when none remains.</param>
    [TestCase(3, 0, 1)]
    [TestCase(3, 1, 2)]
    [TestCase(3, 2, 1)]
    [TestCase(1, 0, -1)]
    public void Close_SelectedDocumentPublishesSurvivorBeforeCallbacks(int documentCount, int selectedIndex, int survivorIndex)
    {
        DocumentManager manager = CreateManager();
        string[] assetPaths = [FirstAssetPath, SecondAssetPath, ThirdAssetPath];
        for (int i = 0; i < documentCount; i++)
        {
            manager.Open(assetPaths[i]);
        }
        FakeDocument selected = _createdDocuments[selectedIndex];
        FakeDocument? survivor = survivorIndex < 0 ? null : _createdDocuments[survivorIndex];
        FakeDocument[] remaining = _createdDocuments.Where(document => document != selected).ToArray();
        manager.Open(selected.AssetPath);
        var closed = new List<AssetDocument>();
        var callbacks = new List<(string Phase, AssetDocument? Active, bool StillListed, int DisposeCount)>();
        selected.OnDisposing = () => callbacks.Add(
            ("dispose", manager.ActiveDocument, manager.Documents.Contains(selected), selected.DisposeCount));
        manager.DocumentClosed += document =>
        {
            closed.Add(document);
            callbacks.Add(("closed", manager.ActiveDocument, manager.Documents.Contains(selected), selected.DisposeCount));
        };

        Assert.That(manager.Close(selected.AssetPath), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(manager.ActiveDocument, Is.SameAs(survivor));
            Assert.That(manager.Documents, Is.EqualTo(remaining));
            Assert.That(manager.FindOpen(selected.AssetPath), Is.Null);
            Assert.That(selected.IsDisposed, Is.True);
            Assert.That(selected.DisposeCount, Is.EqualTo(1));
            Assert.That(remaining.All(document => !document.IsDisposed), Is.True);
            Assert.That(closed, Is.EqualTo(new[] { selected }));
            Assert.That(callbacks.Select(item => item.Phase), Is.EqualTo(new[] { "dispose", "closed" }));
            Assert.That(callbacks.Select(item => item.DisposeCount), Is.EqualTo(new[] { 0, 1 }));
            Assert.That(callbacks.All(item => ReferenceEquals(item.Active, survivor)), Is.True);
            Assert.That(callbacks.Any(item => item.StillListed), Is.False);
        });
    }

    /// <summary>Closing an inactive document preserves the active survivor during disposal and the closed notification.</summary>
    [Test]
    public void Close_InactiveDocumentPreservesActiveDocument()
    {
        DocumentManager manager = CreateManager();
        manager.Open(FirstAssetPath);
        manager.Open(SecondAssetPath);
        FakeDocument first = _createdDocuments[0];
        FakeDocument second = _createdDocuments[1];
        var observedActive = new List<AssetDocument?>();
        first.OnDisposing = () => observedActive.Add(manager.ActiveDocument);
        manager.DocumentClosed += _ => observedActive.Add(manager.ActiveDocument);

        Assert.That(manager.Close(FirstAssetPath), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(manager.ActiveDocument, Is.SameAs(second));
            Assert.That(manager.Documents, Is.EqualTo(new[] { second }));
            Assert.That(observedActive, Is.EqualTo(new[] { second, second }));
            Assert.That(first.DisposeCount, Is.EqualTo(1));
            Assert.That(second.IsDisposed, Is.False);
        });
    }

    /// <summary>Nonforced closure retains dirty documents; forced closure discards them without saving.</summary>
    /// <param name="force">Whether dirty documents should also be closed.</param>
    [TestCase(false)]
    [TestCase(true)]
    public void CloseAll_MixedDocumentsRespectDirtyStateAndForce(bool force)
    {
        DocumentManager manager = CreateManager();
        manager.Open(FirstAssetPath);
        manager.Open(SecondAssetPath);
        manager.Open(ThirdAssetPath);
        FakeDocument first = _createdDocuments[0];
        FakeDocument second = _createdDocuments[1];
        FakeDocument third = _createdDocuments[2];
        first.Modify();
        third.Modify();
        manager.Open(SecondAssetPath);
        var closed = new List<AssetDocument>();
        manager.DocumentClosed += closed.Add;

        bool allClosed = force ? manager.CloseAll(force: true) : manager.CloseAll();

        FakeDocument[] expectedRemaining = force ? [] : [first, third];
        FakeDocument[] expectedClosed = force ? [first, second, third] : [second];
        Assert.Multiple(() =>
        {
            Assert.That(allClosed, Is.EqualTo(force));
            Assert.That(manager.Documents, Is.EqualTo(expectedRemaining));
            Assert.That(manager.ActiveDocument, Is.SameAs(force ? null : third));
            Assert.That(closed, Is.EquivalentTo(expectedClosed));
            Assert.That(expectedClosed.All(document => document.IsDisposed && document.DisposeCount == 1), Is.True);
            Assert.That(expectedRemaining.All(document => !document.IsDisposed && document.DisposeCount == 0), Is.True);
            Assert.That(_createdDocuments.All(document => document.SaveCount == 0), Is.True);
            Assert.That(first.IsDirty, Is.True);
            Assert.That(third.IsDirty, Is.True);
        });
    }

    /// <summary>Closing only clean documents succeeds with either force setting and can be repeated on the empty manager.</summary>
    /// <param name="force">Whether to request forced closure.</param>
    [TestCase(false)]
    [TestCase(true)]
    public void CloseAll_CleanDocumentsAndEmptyManagerReturnTrue(bool force)
    {
        DocumentManager manager = CreateManager();
        manager.Open(FirstAssetPath);
        manager.Open(SecondAssetPath);
        var closed = new List<AssetDocument>();
        manager.DocumentClosed += closed.Add;

        Assert.That(manager.CloseAll(force), Is.True);
        Assert.That(manager.CloseAll(force), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(manager.Documents, Is.Empty);
            Assert.That(manager.ActiveDocument, Is.Null);
            Assert.That(closed, Is.EquivalentTo(_createdDocuments));
            Assert.That(_createdDocuments.All(document => document.IsDisposed && document.DisposeCount == 1), Is.True);
            Assert.That(_createdDocuments.All(document => document.SaveCount == 0), Is.True);
        });
    }

    /// <summary>A nonforced close of only dirty documents leaves selection, ownership and notifications unchanged.</summary>
    [Test]
    public void CloseAll_OnlyDirtyDocumentsStayOpenWithoutForce()
    {
        DocumentManager manager = CreateManager();
        manager.Open(FirstAssetPath);
        manager.Open(SecondAssetPath);
        foreach (FakeDocument document in _createdDocuments)
        {
            document.Modify();
        }
        AssetDocument? active = manager.ActiveDocument;
        var closed = new List<AssetDocument>();
        manager.DocumentClosed += closed.Add;

        Assert.That(manager.CloseAll(), Is.False);

        Assert.Multiple(() =>
        {
            Assert.That(manager.ActiveDocument, Is.SameAs(active));
            Assert.That(manager.Documents, Is.EqualTo(_createdDocuments));
            Assert.That(closed, Is.Empty);
            Assert.That(_createdDocuments.All(document => document.IsDirty && !document.IsDisposed), Is.True);
            Assert.That(_createdDocuments.All(document => document.DisposeCount == 0 && document.SaveCount == 0), Is.True);
        });
    }

    /// <summary>Opening a closed path creates a fresh document and raises both open and activation notifications again.</summary>
    [Test]
    public void Open_AfterCloseCreatesNewInstance()
    {
        DocumentManager manager = CreateManager();
        var opened = new List<AssetDocument>();
        var activated = new List<AssetDocument>();
        var closed = new List<AssetDocument>();
        manager.DocumentOpened += opened.Add;
        manager.DocumentActivated += activated.Add;
        manager.DocumentClosed += closed.Add;
        manager.Open(FirstAssetPath);
        FakeDocument original = _createdDocuments[0];
        Assert.That(manager.Close(FirstAssetPath), Is.True);
        Assert.That(manager.FindOpen(FirstAssetPath), Is.Null);

        manager.Open(FirstAssetPath);

        Assert.That(_createdDocuments, Has.Count.EqualTo(2));
        FakeDocument reopened = _createdDocuments[1];
        Assert.Multiple(() =>
        {
            Assert.That(reopened, Is.Not.SameAs(original));
            Assert.That(reopened.AssetPath, Is.EqualTo(original.AssetPath));
            Assert.That(manager.ActiveDocument, Is.SameAs(reopened));
            Assert.That(manager.FindOpen(FirstAssetPath), Is.SameAs(reopened));
            Assert.That(manager.Documents, Is.EqualTo(new[] { reopened }));
            Assert.That(original.IsDisposed, Is.True);
            Assert.That(original.DisposeCount, Is.EqualTo(1));
            Assert.That(reopened.IsDisposed, Is.False);
            Assert.That(reopened.IsDirty, Is.False);
            Assert.That(opened, Is.EqualTo(new[] { original, reopened }));
            Assert.That(activated, Is.EqualTo(new[] { original, reopened }));
            Assert.That(closed, Is.EqualTo(new[] { original }));
        });
    }

    private DocumentManager CreateManager()
    {
        var context = new EditorContext(null!, AlcoProject.CreateUntitled(_tempRoot));
        var registry = new DocumentRegistry(context);
        registry.Register(".testdoc", new DelegateDocumentFactory((documentContext, assetPath) =>
        {
            var document = new FakeDocument(documentContext, assetPath);
            _createdDocuments.Add(document);
            return document;
        }));
        return new DocumentManager(context, registry);
    }

    private sealed class FakeDocument : AssetDocument
    {
        /// <summary>Creates a document whose only dependency is the project's ownership information.</summary>
        /// <param name="context">The editor context with an isolated project and no engine.</param>
        /// <param name="assetPath">The owned asset path represented by this document.</param>
        public FakeDocument(EditorContext context, string assetPath) : base(context, assetPath)
        {
        }

        /// <summary>The observer invoked at the start of explicit disposal.</summary>
        public Action? OnDisposing { get; set; }

        /// <summary>The number of completed explicit disposals.</summary>
        public int DisposeCount { get; private set; }

        /// <summary>The number of save attempts.</summary>
        public int SaveCount { get; private set; }

        /// <summary>Marks this document as holding unsaved changes.</summary>
        public void Modify() => MarkDirty();

        /// <summary>Records a save attempt and clears dirty state only for an editable document.</summary>
        public override void Save()
        {
            SaveCount++;
            if (IsReadOnly)
            {
                return;
            }
            IsDirty = false;
        }

        protected override void DrawContent()
        {
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposing)
            {
                return;
            }
            OnDisposing?.Invoke();
            DisposeCount++;
            base.Dispose(disposing);
        }
    }
}
