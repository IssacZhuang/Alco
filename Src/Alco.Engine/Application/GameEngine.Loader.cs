using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
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
            yield return new AssetLoaderFontTTF(RenderingSystem, BuiltInAssets.Shader_TextSdf, generateSdf: false, cacheDirectory: CreateFontCacheDirectory(Setting.Graphics));
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
        // Deployed engine built-ins (shaders, fonts, render nodes) sit next to the
        // executable: serve them as a low-priority fallback so name resolution still
        // works when the working directory differs from the output directory
        // (dotnet run, IDE default debug CWD). Same-named assets in the primary
        // asset root shadow the fallback.
        yield return new DeployedAssetFileSource();
    }

    /// <summary>Read-only source over the executable-adjacent built-in assets directory.</summary>
    private sealed class DeployedAssetFileSource : DirectoryFileSource
    {
        public DeployedAssetFileSource()
            : base(Path.Combine(AppContext.BaseDirectory, "Assets"))
        {
        }

        /// <summary>Below the primary asset root (5): fills gaps, never shadows it.</summary>
        public override int Priority => 1;
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
        yield return new JsonConverterMaterialAsset(AssetSystem);
        yield return new JsonConverterDepthStencilState();
        yield return new JsonConverterBlendState();
        yield return new JsonConverterPivot();
        yield return new JsonStringEnumConverter();
        yield return new JsonConverterPadding();
        yield return new JsonConverterCurvePointFactory();
    }

    /// <summary>
    /// Creates the JSON serializer options shared by agent-facing surfaces (tool
    /// argument deserialization, HTTP responses): camelCase naming configured with the
    /// engine's default JSON converters. Hosts and the agent control protocol use this
    /// so every surface serializes engine types identically.
    /// </summary>
    public JsonSerializerOptions CreateAgentJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };

        foreach (var converter in CreateDefaultJsonConverters())
        {
            options.Converters.Add(converter);
        }

        return options;
    }
}
