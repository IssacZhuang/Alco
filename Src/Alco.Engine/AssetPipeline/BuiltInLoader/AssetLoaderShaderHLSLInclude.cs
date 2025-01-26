using System.Diagnostics.CodeAnalysis;
using System.Text;
using Alco.Rendering;
using Alco.IO;

namespace Alco.Engine;

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
    public string CreateAsset(string filename, ReadOnlySpan<byte> file)
    {
        return Encoding.UTF8.GetString(file);
    }
}
