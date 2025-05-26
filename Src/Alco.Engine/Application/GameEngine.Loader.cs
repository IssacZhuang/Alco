using System.Text;
using Alco.Rendering;
using Alco.IO;
using System.Text.Json;

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
    }

    public virtual IEnumerable<IAssetHotReloader> CreateDefaultAssetHotReloaders()
    {
        yield return new AssetHotReloaderShaderHLSL((string includeName) =>
        {
            if (Assets.TryLoadRaw(includeName, out SafeMemoryHandle data))
            {
                return Encoding.UTF8.GetString(data.AsReadOnlySpan());
            }
            throw new Exception($"Can not find the include file: {includeName}");
        });

        yield return new AssetHotReloaderTexture2D(Rendering);
    }

    public virtual IEnumerable<IAssetEncoder> CreateDefaultAssetEncoders()
    {
        yield return new AssetEncoderConfig(ConfigSerializeOption);
    }

    public virtual IEnumerable<IFileSource> CreateDefaultFileSources()
    {
        yield return new DirectoryFileSource(Setting.Assets.AssetsPath);
    }

    protected virtual JsonSerializerOptions CreateConfigSerializeOption()
    {
        var options = new JsonSerializerOptions()
        {
            TypeInfoResolver = new ConfigJsonTypeResolver(),
            WriteIndented = true,
            Converters = {
                new JsonConverterType(),
                new JsonConverterVector2(),
                new JsonConverterVector3(),
                new JsonConverterVector4(),
                new JsonConverterHalf2(),
                new JsonConverterHalf3(),
                new JsonConverterHalf4(),
                new JsonConverterInt2(),
                new JsonConverterInt3(),
                new JsonConverterInt4(),
                new JsonConverterUInt2(),
                new JsonConverterUInt3(),
                new JsonConverterUInt4(),
                new JsonConverterQuaternion(),
                new JsonConverterColor32(),
                new JsonConverterColorFloat(),
            }
        };

        options.MakeReadOnly();

        return options;
    }


}