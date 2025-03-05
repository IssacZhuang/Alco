using System.Diagnostics.CodeAnalysis;
using System.Text;
using Alco.Rendering;
using Alco.IO;


namespace Alco.Engine;

/// <summary>
/// Convert a shader text file to a shader object. This loader will compile the shader to SPIR-V and create a GPU shader object from it
/// </summary>
public class AssetLoaderShaderHLSL : IAssetLoader
{
    private static readonly string[] Extensions = [FileExt.ShaderHLSL];
    private readonly Func<string, string>? _includeResolver;
    private readonly RenderingSystem _renderingSystem;

    public string Name => "AssetLoader.Shader.HLSL";

    public IReadOnlyList<string> FileExtensions => Extensions;

    public AssetLoaderShaderHLSL(RenderingSystem renderingSystem, Func<string, string>? includeResolver = null)
    {
        _includeResolver = includeResolver;
        _renderingSystem = renderingSystem;
    }

    public bool CanHandleType(Type type)
    {
        return type == typeof(Shader) || type == typeof(string);
    }

    /// <inheritdoc/>
    public object CreateAsset(in AssetLoadContext context)
    {
        if (context.AssetType == typeof(Shader))
        {
            return CreateShader(context);
        }
        else if (context.AssetType == typeof(string))
        {
            return Encoding.UTF8.GetString(context.Data);
        }
        throw new InvalidOperationException($"Cannot create asset of type {context.AssetType.Name}");
    }

    private object CreateShader(in AssetLoadContext context)
    {
        //ShaderCompileResultDeprecated preprocessed = UtilsShaderHLSL.Compile(Encoding.UTF8.GetString(file), filename, _includeResolver);
        IncludeHelper includeHelper = new IncludeHelper();
        string shaderText = Encoding.UTF8.GetString(context.Data);
        if (_includeResolver != null)
        {
            shaderText = includeHelper.ProcessInclude(shaderText, context.Filename, _includeResolver);
        }
        else
        {
            AssetSystem assetSystem = context.AssetSystem;
            shaderText = includeHelper.ProcessInclude(shaderText, context.Filename, (string include) =>
            {
                return assetSystem.Load<string>(include);
            });
        }

        return _renderingSystem.CreateShader(shaderText, context.Filename);
    }
}
