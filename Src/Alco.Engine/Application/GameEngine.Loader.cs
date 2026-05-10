using System.Text;
using Alco.Rendering;
using Alco.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alco.Engine;

public partial class GameEngine
{
    private DirectoryOptionCache<Texture2DImportOption>? _textureOptionCache;

    public virtual IEnumerable<IAssetLoader> CreateDefaultAssetLoaders()
    {
        // Create JSON converters (shared between meta loader and option cache)
        var jsonConverters = CreateDefaultJsonConverters();
        var jsonConvertersList = jsonConverters.ToList();

        // Create texture directory option cache
        _textureOptionCache = new DirectoryOptionCache<Texture2DImportOption>(
            AssetSystem,
            ".texture-option.meta",
            jsonConvertersList,
            Texture2DImportOptionMerge);

        // shader
        yield return new AssetLoaderShaderHLSLInclude();
        yield return new AssetLoaderShaderHLSL(RenderingSystem);

        // texture
        if (Setting.HasGPU)
        {
            yield return new AssetLoaderFontTTF(RenderingSystem, BuiltInAssets.Shader_TextSDF, generateSdf: false);
            yield return new AssetLoaderTexture2D(RenderingSystem, _textureOptionCache);
        }
        else
        {
            yield return new AssetLoaderFontTTFNoGPU(RenderingSystem);
            yield return new AssetLoaderTexture2DNoGPU(RenderingSystem, _textureOptionCache);
        }

        // audio
        yield return new AssetLoaderAudioVorbis(AudioDevice);
        yield return new AssetLoaderAudioWave(AudioDevice);
        yield return new AssetLoaderAudioFlac(AudioDevice);

        //meta
        yield return new AssetLoaderMeta(jsonConvertersList);
    }

    /// <summary>
    /// Merges two <see cref="Texture2DImportOption"/> instances.
    /// Child values override parent values when non-null.
    /// </summary>
    private static Texture2DImportOption Texture2DImportOptionMerge(Texture2DImportOption parent, Texture2DImportOption child)
    {
        return new Texture2DImportOption
        {
            FilterMode = child.FilterMode ?? parent.FilterMode,
            AddressMode = child.AddressMode ?? parent.AddressMode,
            SlicePadding = child.SlicePadding ?? parent.SlicePadding,
        };
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
        yield return new JsonConverterFont(AssetSystem);
        yield return new JsonConverterDepthStencilState();
        yield return new JsonConverterBlendState();
        yield return new JsonConverterPivot();
        yield return new JsonStringEnumConverter();
        yield return new JsonConverterPadding();
        yield return new JsonConverterCurvePointFactory();
    }
}
