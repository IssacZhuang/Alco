using System.Diagnostics.CodeAnalysis;
using System.Text;
using Alco.Rendering;
using Alco.IO;

namespace Alco.Engine;

/// <summary>
/// Represents an asset loader for HLSL include files.
/// </summary>
public class AssetLoaderShaderHLSLInclude : IAssetLoader
{
    private static readonly string[] Extensions = new string[] { FileExt.ShaderHLSLInclude };

    /// <inheritdoc/>
    public string Name => "AssetLoader.Shader.HLSLInclude";

    /// <inheritdoc/>
    public IReadOnlyList<string> FileExtensions => Extensions;

    public bool CanHandleType(Type type)
    {
        return type == typeof(string);
    }

    /// <inheritdoc/>
    public object CreateAsset(string filename, ReadOnlySpan<byte> file, Type targetType)
    {
        return Encoding.UTF8.GetString(file);
    }
}
