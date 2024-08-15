using System.Diagnostics.CodeAnalysis;
using System.Text;
using Vocore.Rendering;
using Vocore.IO;

namespace Vocore.Engine;

/// <summary>
/// Represents an asset loader for HLSL include files.
/// </summary>
public class AssetLoaderShaderHLSLInclude : IAssetLoader<string>
{
    private static readonly string[] Extensions = new string[] { FileExt.ShaderHLSLInclude };

    /// <inheritdoc/>
    public string Name => "AssetLoader.Shader.HLSLInclude";

    /// <inheritdoc/>
    public IReadOnlyList<string> FileExtensions => Extensions;

    /// <inheritdoc/>
    public bool TryCreateAsset(string filename, ReadOnlySpan<byte> file, [NotNullWhen(true)] out string? asset)
    {
        asset = Encoding.UTF8.GetString(file); 
        return true;
    }
}
