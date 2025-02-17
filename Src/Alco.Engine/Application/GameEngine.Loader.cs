using System.Text;
using Alco.Rendering;

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
        Assets.RegisterAssetLoader(new AssetLoaderShaderHLSL(Rendering, (string includeName) =>
        {
            if (Assets.TryLoadRaw(includeName, out SafeMemoryHandle data))
            {
                return Encoding.UTF8.GetString(data.Span);
            }
            throw new Exception($"Can not find the include file: {includeName}");
        }));
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
        Assets.RegisterAssetLoader(new AssetLoaderConfig(Assets));
    }
}