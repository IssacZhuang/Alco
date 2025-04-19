using System.Text;
using Alco.Rendering;
using Alco.IO;

namespace Alco.Engine;

public partial class GameEngine
{
    public virtual IEnumerable<IAssetLoader> CreateDefaultAssetLoaders()
    {
        // texture
        yield return new AssetLoaderFontTTF(Rendering);
        yield return new AssetLoaderTexture2D(Rendering);

        // shader
        yield return new AssetLoaderShaderHLSLInclude();
        yield return new AssetLoaderShaderHLSL(Rendering);

        // audio
        yield return new AssetLoaderAudioVorbis(AudioDevice);
        yield return new AssetLoaderAudioWave(AudioDevice);
        yield return new AssetLoaderAudioFlac(AudioDevice);

        // config
        var configReferenceResolver = new ConfigReferenceResolver(Assets);
        var jsonSerializerOptions = Configable.BuildJsonSerializerOptions(configReferenceResolver);
        jsonSerializerOptions.WriteIndented = true;
        yield return new AssetLoaderConfig(jsonSerializerOptions, configReferenceResolver);
    }

    public virtual IEnumerable<IAssetHotReloader> CreateDefaultAssetHotReloaders()
    {
        yield return new AssetHotReloaderShaderHLSL((string includeName) =>
        {
            if (Assets.TryLoadRaw(includeName, out SafeMemoryHandle data))
            {
                return Encoding.UTF8.GetString(data.Span);
            }
            throw new Exception($"Can not find the include file: {includeName}");
        });

        yield return new AssetHotReloaderTexture2D(Rendering);
    }

    public virtual IEnumerable<IAssetEncoder> CreateDefaultAssetEncoders()
    {
        var configReferenceResolver = new ConfigReferenceResolver(Assets);
        var jsonSerializerOptions = Configable.BuildJsonSerializerOptions(configReferenceResolver);
        jsonSerializerOptions.WriteIndented = true;
        yield return new AssetEncoderConfig(jsonSerializerOptions);
    }

    public virtual IEnumerable<IFileSource> CreateDefaultFileSources()
    {
        yield return new DirectoryFileSource(Setting.Assets.AssetsPath);
    }


}