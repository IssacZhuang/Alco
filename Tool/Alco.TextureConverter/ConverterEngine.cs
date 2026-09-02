using Alco.Engine;
using Alco.Graphics;

namespace Alco.TextureConverter;

/// <summary>
/// A <see cref="GameEngine"/> host for batch conversions: headless GPU (compute
/// compressors), no audio, no main loop. The engine is constructed once and
/// reused across <see cref="TextureConverter.TryConvert"/> calls — texture
/// creation, compression and readback are direct GPU calls that do not need a
/// running platform loop.
/// </summary>
internal sealed class ConverterEngine : GameEngine
{
	public ConverterEngine()
		: base(CreateSetting())
	{
	}

	private static GameEngineSetting CreateSetting()
	{
		GameEngineSetting setting = GameEngineSetting.CreateGPUWithoutView();
		// Match the proven headless configuration of the game application.
		setting.Graphics = GraphicsSetting.Default with { Backend = GraphicsBackend.WGPUVulkan };
		setting.Audio = AudioSetting.NoAudio;
		return setting;
	}
}
