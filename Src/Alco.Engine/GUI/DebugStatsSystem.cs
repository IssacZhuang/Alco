using System.Numerics;

namespace Alco.Engine;

/// <summary>
/// Engine system that owns the <see cref="DebugStatsRenderer"/> and drives the
/// debug-stats overlay lifecycle automatically. Producers call <see cref="DebugStats.Text"/>
/// etc. from anywhere during the frame; this system handles begin (lazy), target
/// assignment, and end-of-frame submission without any manual calls from game code.
/// </summary>
public class DebugStatsSystem : BaseEngineSystem
{
    private readonly GameEngine _engine;
    private readonly DebugStatsRenderer _renderer;
    private readonly Action<uint2> _resizeHandler;

    /// <summary>
    /// Runs just before <see cref="ImGUISystem"/> so the overlay sits below ImGui.
    /// </summary>
    public override int Order => int.MaxValue - 1;

    public DebugStatsSystem(GameEngine engine)
    {
        _engine = engine;

        BuiltInAssets assets = engine.BuiltInAssets;
        View view = engine.MainView;

        _renderer = new DebugStatsRenderer(
            engine.Input, view, view.Size.X, view.Size.Y,
            engine.RenderingSystem,
            assets.Shader_Text, assets.Shader_Sprite);

        DebugStatsStyle style = new DebugStatsStyle
        {
            Font = assets.Font_Default,
            FontSize = 16,
            SliderWidth = 140,
            SliderThumbWidth = 16,
            SliderColor = 0x2a2a2a,
            SliderThumbColor = 0x373737,
            SliderThumbHoverColor = 0x525252,
            SliderThumbDragColor = 0x234A6C,
            TextColor = 0xf1f1f1,
            ButtonColor = 0x2a2a2a,
            ButtonHoverColor = 0x3a3a3a,
            ButtonPressedColor = 0x234A6C,
            CheckBoxColor = 0x2a2a2a,
            CheckBoxHoverColor = 0x3a3a3a,
            CheckBoxCheckColor = 0x007ACC,
            Margin = new Vector4(2, 2, 2, 2),
            Padding = new Vector2(10, 4)
        };
        DebugStats.Initialize(_renderer, style);

        _resizeHandler = size => _renderer.SetResolution(size.X, size.Y);
        engine.MainPresenter.OnResize += _resizeHandler;
    }

    public override void OnBeginFrame(float deltaTime)
    {
        _renderer.Target = _engine.MainPresenter.FrameBuffer;
    }

    public override void OnEndFrame(float deltaTime)
    {
        DebugStats.CheckAndSubmit();
    }

    public override void Dispose()
    {
        _engine.MainPresenter.OnResize -= _resizeHandler;
        _renderer.Dispose();
    }
}
