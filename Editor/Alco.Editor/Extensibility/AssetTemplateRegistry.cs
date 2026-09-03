namespace Alco.Editor.Extensibility;

/// <summary>
/// A template behind one entry of the asset browser's "new asset" menu: the menu
/// label, the file extension, and the initial file content.
/// </summary>
public interface IAssetTemplate
{
    /// <summary>The menu label (e.g. <c>"Particle Effect 2D (.afx)"</c>).</summary>
    string DisplayName { get; }

    /// <summary>The file extension of the created asset (e.g. <c>.afx</c>).</summary>
    string FileExtension { get; }

    /// <summary>The base file name for a new asset (e.g. <c>NewEffect2D</c>); the
    /// browser appends a numeric suffix when the name is taken.</summary>
    string BaseName { get; }

    /// <summary>Creates the initial file content for a new asset.</summary>
    /// <param name="assetName">The final asset file name without extension (suffix included).</param>
    string CreateContent(string assetName);
}

/// <summary>
/// The asset templates offered by the asset browser's "new asset" menu, in
/// registration order.
/// </summary>
public sealed class AssetTemplateRegistry
{
    private readonly List<IAssetTemplate> _templates = new();

    /// <summary>The registered templates, in registration order.</summary>
    public IReadOnlyList<IAssetTemplate> Templates => _templates;

    /// <summary>Registers an asset template.</summary>
    public void Register(IAssetTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        _templates.Add(template);
    }
}
