using System.Text;
using Alco.Rendering;
using Alco.IO;

namespace Alco.Engine;

public partial class GameEngine
{
    protected virtual void InitializeDefaultAssetLoader(GameEngineSetting setting)
    {
        //texture
        Assets.RegisterAssetLoader(new AssetLoaderFontTTF(Rendering));
        Assets.RegisterAssetLoader(new AssetLoaderTexture2D(Rendering));

        //shader
        Assets.RegisterAssetLoader(new AssetLoaderShaderHLSLInclude());
        Assets.RegisterAssetLoader(new AssetLoaderShaderHLSL(Rendering));
        //Assets.RegisterAssetLoader(new AssetLoaderShaderSlang(Rendering, Assets));

        //aduio
        Assets.RegisterAssetLoader(new AssetLoaderAudioVorbis(AudioDevice));
        //Assets.RegisterAssetLoader(new AssetLoaderAudioMpge(AudioDevice));
        Assets.RegisterAssetLoader(new AssetLoaderAudioWave(AudioDevice));
        Assets.RegisterAssetLoader(new AssetLoaderAudioFlac(AudioDevice));
        //Assets.RegisterAssetLoader(new AssetLoaderAudioAiff(AudioDevice));

        Assets.RegisterAssetHotReloader<Shader>(new AssetHotReloaderShaderHLSL((string includeName) =>
        {
            if (Assets.TryLoadRaw(includeName, out SafeMemoryHandle data))
            {
                return Encoding.UTF8.GetString(data.Span);
            }
            throw new Exception($"Can not find the include file: {includeName}");
        }));

        Assets.RegisterAssetHotReloader<Texture2D>(new AssetHotReloaderTexture2D(Rendering));

        var configReferenceResolver = new ConfigReferenceResolver(Assets);
        var jsonSerializerOptions = BaseConfig.BuildJsonSerializerOptions(configReferenceResolver);

        Assets.RegisterAssetLoader(new AssetLoaderConfig(jsonSerializerOptions, configReferenceResolver));
        Assets.RegisterAssetEncoder(new AssetEncoderConfig(jsonSerializerOptions));
    }
}