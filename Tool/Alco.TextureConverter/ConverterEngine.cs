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
		// Pick the backend every host can serve headlessly: D3D12 falls back to
		// the WARP software adapter on GPU-less CI Windows, Vulkan needs a
		// driver (lavapipe on CI Linux) and does not exist on macOS at all.
		setting.Graphics = GraphicsSetting.Default with
		{
			Backend = OperatingSystem.IsMacOS() ? GraphicsBackend.Metal
				: OperatingSystem.IsWindows() ? GraphicsBackend.Dx12
				: GraphicsBackend.Vulkan,
		};
		setting.Audio = AudioSetting.NoAudio;
		return setting;
	}
}
