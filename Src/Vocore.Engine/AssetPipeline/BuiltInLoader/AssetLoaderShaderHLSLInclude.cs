using System.Diagnostics.CodeAnalysis;
using System.Text;
using Vocore.Rendering;
using Vocore.IO;

namespace Vocore.Engine;

/// <summary>
/// Represents an asset loader for HLSL include files.
/// </summary>
public class AssetLoaderShaderHLSLInclude : BaseAssetLoader<string>
{
    private static readonly string[] Extensions = new string[] { FileExt.ShaderHLSLInclude };

    /// <inheritdoc/>
    public override string Name => "AssetLoader.Shader.HLSLInclude";

    /// <inheritdoc/>
    public override IReadOnlyList<string> FileExtensions => Extensions;

    /// <inheritdoc/>
    protected override bool TryCreateAssetCore(string filename, ReadOnlySpan<byte> file, [NotNullWhen(true)] out string? asset)
    {
        asset = Encoding.UTF8.GetString(file); 
        return true;
    }
}
