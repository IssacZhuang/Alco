using System.Text.Json.Serialization;
using Alco.Rendering;
using Alco.IO;

namespace Alco.Engine;

public partial class GameEngine
{
    public virtual IEnumerable<IAssetLoader> CreateDefaultAssetLoaders()
    {
        // Create JSON converters (shared between meta loader and option cache)
        var jsonConverters = CreateDefaultJsonConverters();
        var jsonConvertersList = jsonConverters.ToList();

        // material
        yield return new AssetLoaderMaterialAsset(AssetSystem, RenderingSystem.ShaderSystem);

        // render node factories (shader bindings for render nodes)
        yield return new AssetLoaderRenderNodeFactory(RenderingSystem.ShaderSystem);

        // texture — loaders create their own option cache internally
        if (Setting.HasGPU)
        {
            yield return new AssetLoaderFontTTF(RenderingSystem, BuiltInAssets.Shader_TextSdf, generateSdf: false);
            yield return new AssetLoaderTexture2D(RenderingSystem, AssetSystem);
        }
        else
        {
            yield return new AssetLoaderFontTTFNoGPU(RenderingSystem);
            yield return new AssetLoaderTexture2DNoGPU(RenderingSystem, AssetSystem);
        }

        // audio
        if (Setting.HasAudio)
        {
            yield return new AssetLoaderAudioVorbis(AudioDevice);
            yield return new AssetLoaderAudioWave(AudioDevice);
            yield return new AssetLoaderAudioFlac(AudioDevice);
        }
        else
        {
            yield return new AssetLoaderAudioNoLoad(AudioDevice);
        }

        //meta
        yield return new AssetLoaderMeta(jsonConvertersList);
    }

    public virtual IEnumerable<IAssetHotReloader> CreateDefaultAssetHotReloaders()
    {
        yield return new AssetHotReloaderTexture2D(RenderingSystem);

        if (Setting.HasAudio)
        {
            yield return new AssetHotReloaderAudioVorbis(AudioDevice);
        }
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
        yield return new JsonConverterTexture2D(AssetSystem);
        yield return new JsonConverterFont(AssetSystem);
        yield return new JsonConverterShader(RenderingSystem.ShaderSystem);
        yield return new JsonConverterShaderLibrary(RenderingSystem.ShaderSystem);
        yield return new JsonConverterDepthStencilState();
        yield return new JsonConverterBlendState();
        yield return new JsonConverterPivot();
        yield return new JsonStringEnumConverter();
        yield return new JsonConverterPadding();
        yield return new JsonConverterCurvePointFactory();
    }
}
