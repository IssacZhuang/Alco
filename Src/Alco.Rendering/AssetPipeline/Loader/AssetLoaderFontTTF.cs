using Alco.IO;

namespace Alco.Rendering;

/// <summary>Asset loader for TrueType and OpenType font files producing immutable CPU <see cref="Font"/> assets.</summary>
public class AssetLoaderFontTTF : BaseAssetLoader<Font>
{
    private static readonly string[] Extensions = [FileExt.FontTrueType];

    /// <inheritdoc/>
    public override string Name => "AssetLoader.Font.TTF";

    /// <inheritdoc/>
    public override IReadOnlyList<string> FileExtensions => Extensions;

    /// <inheritdoc/>
    public override object CreateAsset(in AssetLoadContext context)
    {
        return Font.Create(context.GetData(), context.Filename);
    }
}
