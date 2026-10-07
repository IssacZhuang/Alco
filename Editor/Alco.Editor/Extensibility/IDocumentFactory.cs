namespace Alco.Editor.Extensibility;

/// <summary>
/// Creates an <see cref="AssetDocument"/> for one asset path. Registered in the
/// <see cref="DocumentRegistry"/> under the file extension the document edits.
/// </summary>
public interface IDocumentFactory
{
    /// <summary>Creates the document editing <paramref name="assetPath"/>.</summary>
    /// <param name="context">The shared editor services.</param>
    /// <param name="assetPath">The asset-system-relative path to open.</param>
    AssetDocument Create(EditorContext context, string assetPath);
}
