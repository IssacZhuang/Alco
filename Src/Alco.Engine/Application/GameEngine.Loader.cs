using System.Text;
using Alco.Rendering;
using Alco.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alco.Engine;

public partial class GameEngine
{
    public virtual IEnumerable<IAssetLoader> CreateDefaultAssetLoaders()
    {
        // texture
        yield return new AssetLoaderFontTTF(RenderingSystem);
        yield return new AssetLoaderTexture2D(RenderingSystem);

        // shader
        yield return new AssetLoaderShaderHLSLInclude();
        yield return new AssetLoaderShaderHLSL(RenderingSystem);

        // audio
        yield return new AssetLoaderAudioVorbis(AudioDevice);
        yield return new AssetLoaderAudioWave(AudioDevice);
        yield return new AssetLoaderAudioFlac(AudioDevice);

        //meta
        yield return new AssetLoaderMeta(CreateDefaultJsonConverters());
    }

    public virtual IEnumerable<IAssetHotReloader> CreateDefaultAssetHotReloaders()
    {
        yield return new AssetHotReloaderShaderHLSL((string includeName) =>
        {
            if (AssetSystem.TryLoadRaw(includeName, out SafeMemoryHandle data))
            {
                return Encoding.UTF8.GetString(data.AsReadOnlySpan());
            }
            throw new Exception($"Can not find the include file: {includeName}");
        });

        yield return new AssetHotReloaderTexture2D(RenderingSystem);
    }

    public virtual IEnumerable<IFileSource> CreateDefaultFileSources()
    {
        yield return new DirectoryFileSource(Setting.Assets.AssetsPath);
    }

    public virtual IEnumerable<JsonConverter> CreateDefaultJsonConverters()
    {
        yield return new JsonConverterType();
        yield return new JsonConverterVector2();
        yield return new JsonConverterVector3();
        yield return new JsonConverterVector4();
        yield return new JsonConverterHalf2();
        yield return new JsonConverterHalf3();
        yield return new JsonConverterHalf4();
        yield return new JsonConverterInt2();
        yield return new JsonConverterInt3();
        yield return new JsonConverterInt4();
        yield return new JsonConverterUInt2();
        yield return new JsonConverterUInt3();
        yield return new JsonConverterUInt4();
        yield return new JsonConverterQuaternion();
        yield return new JsonConverterColor32();
        yield return new JsonConverterColorFloat();
        yield return new JsonConverterShader(AssetSystem);
        yield return new JsonConverterTexture2D(AssetSystem);
        yield return new JsonConverterDepthStencilState();
        yield return new JsonConverterBlendState();
        yield return new JsonConverterPivot();
        yield return new JsonStringEnumConverter();
    }
}