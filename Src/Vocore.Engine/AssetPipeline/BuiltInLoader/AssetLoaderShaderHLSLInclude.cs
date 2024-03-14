using System.Diagnostics.CodeAnalysis;
using System.Text;
using Vocore.Rendering;

namespace Vocore.Engine;

/// <summary>
/// Represents an asset loader for HLSL include files.
/// </summary>
public class AssetLoaderShaderHLSLInclude : BaseAssetLoader<string, string>
{
    private static readonly string[] Extensions = new string[] { ".hlsli" };

    /// <inheritdoc/>
    public override string Name => "AssetLoader.HLSLInclude";

    /// <inheritdoc/>
    public override IReadOnlyList<string> FileExtensions => Extensions;

    /// <inheritdoc/>
    protected override bool TryAsyncPreprocessCore(string filename, byte[] file, [NotNullWhen(true)] out string? preprocessed)
    {
        preprocessed = Encoding.UTF8.GetString(file);
        return true;
    }

    /// <inheritdoc/>
    protected override bool TryCreateAssetCore(string filename, string preprocessed, [NotNullWhen(true)] out string? asset)
    {
        asset = preprocessed; 
        return true;
    }
}
