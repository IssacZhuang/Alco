namespace Alco.Editor.Extensibility;

/// <summary>
/// The document factories the <see cref="DocumentManager"/> routes asset opens through,
/// keyed by file extension (e.g. <c>.amat</c>) with ordinal-ignore-case matching.
/// A later registration replaces an earlier one for the same extension.
/// </summary>
public sealed class DocumentRegistry
{
    private readonly EditorContext _context;
    private readonly Dictionary<string, IDocumentFactory> _factories = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates the registry over the given editor context.</summary>
    public DocumentRegistry(EditorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <summary>Registers the document factory for a file extension (e.g. <c>.amat</c>).</summary>
    public void Register(string extension, IDocumentFactory factory)
    {
        ArgumentNullException.ThrowIfNull(extension);
        ArgumentNullException.ThrowIfNull(factory);
        _factories[extension] = factory;
    }

    /// <summary>
    /// Creates the document for <paramref name="assetPath"/> using the factory
    /// registered for <paramref name="extension"/>, or null when none is registered.
    /// </summary>
    public AssetDocument? TryCreate(string extension, string assetPath)
    {
        return _factories.TryGetValue(extension, out IDocumentFactory? factory)
            ? factory.Create(_context, assetPath)
            : null;
    }
}

/// <summary>
/// An <see cref="IDocumentFactory"/> backed by a delegate, for document types whose
/// creation needs no extra state.
/// </summary>
public sealed class DelegateDocumentFactory : IDocumentFactory
{
    private readonly Func<EditorContext, string, AssetDocument> _create;

    /// <summary>Creates the factory wrapping the given delegate.</summary>
    public DelegateDocumentFactory(Func<EditorContext, string, AssetDocument> create)
    {
        ArgumentNullException.ThrowIfNull(create);
        _create = create;
    }

    /// <inheritdoc />
    public AssetDocument Create(EditorContext context, string assetPath) => _create(context, assetPath);
}
