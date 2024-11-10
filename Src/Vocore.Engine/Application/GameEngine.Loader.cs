using System.Text;

namespace Vocore.Engine;

public partial class GameEngine
{
    private void InitializeDefaultAssetLoader()
    {
        //texture
        Assets.RegisterAssetLoader(new AssetLoaderFontTTF(Rendering));
        Assets.RegisterAssetLoader(new AssetLoaderTexture2D(Rendering));

        //shader
        Assets.RegisterAssetLoader(new AssetLoaderShaderHLSLInclude());
        Assets.RegisterAssetLoader(new AssetLoaderShaderHLSL(Rendering, (string includeName) =>
        {
            if (Assets.TryLoadRaw(includeName, out ReadOnlySpan<byte> data))
            {
                return Encoding.UTF8.GetString(data);
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
    }
}