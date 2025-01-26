// using System.Diagnostics.CodeAnalysis;
// using System.Text;
// using Alco.Rendering;
// using Alco.IO;
// using SlangSharp;


// namespace Alco.Engine;

// /// <summary>
// /// Convert a shader text file to a shader object. This loader will compile the shader to SPIR-V and create a GPU shader object from it
// /// </summary>
// public class AssetLoaderShaderSlang : IAssetLoader<ShaderDeprecated>
// {
//     private static readonly string[] Extensions = new string[] { FileExt.ShaderSlang };

//     private readonly RenderingSystem _renderingSystem;

//     public string Name => "AssetLoader.Shader.Slang";

//     public IReadOnlyList<string> FileExtensions => Extensions;

//     private SlangAssetFileSystem _fileSystem;

//     public AssetLoaderShaderSlang(RenderingSystem renderingSystem, AssetSystem assetSystem)
//     {
//         _fileSystem = new SlangAssetFileSystem(assetSystem);
//         _renderingSystem = renderingSystem;
//     }

//     /// <inheritdoc/>
//     public bool TryCreateAsset(string filename, ReadOnlySpan<byte> file, [NotNullWhen(true)] out ShaderDeprecated? asset)
//     {
//         ShaderCompileResultDeprecated preprocessed = UtilsShaderSlang.Compile(Encoding.UTF8.GetString(file), filename, _fileSystem);
//         asset = _renderingSystem.CreateShader(preprocessed); 
//         return true;
//     }

//     private class SlangAssetFileSystem : BaseSlangFileSystem
//     {
//         private readonly AssetSystem _assetSystem;
//         public SlangAssetFileSystem(AssetSystem assetSystem)
//         {
//             _assetSystem = assetSystem;
//         }
//         public override bool TryLoadFile(string path, out ReadOnlySpan<byte> data)
//         {
//             return _assetSystem.TryLoadRaw(path, out data);
//         }
//     }

// }
