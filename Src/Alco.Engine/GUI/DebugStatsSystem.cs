namespace Alco.Engine;

/// <summary>
/// Engine system that owns the <see cref="DebugStatsRenderer"/> and drives the
/// debug-stats overlay lifecycle automatically.
/// </summary>
public sealed class DebugStatsSystem : BaseEngineSystem
{
    private readonly GameEngine _engine;
    private readonly DebugStatsRenderer _renderer;
    private readonly Action<uint2> _resizeHandler;

    /// <summary>
    /// Runs just before <c>ImGUISystem</c> so the overlay sits below ImGui.
    /// </summary>
    public override int Order => int.MaxValue - 1;

    /// <summary>
    /// Initializes a new instance of <see cref="DebugStatsSystem"/>.
    /// </summary>
    /// <param name="engine">The engine that owns the main view and rendering system.</param>
    public DebugStatsSystem(GameEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        _engine = engine;

        View view = engine.MainView;
        _renderer = new DebugStatsRenderer(engine.Input, view, view.Size.X, view.Size.Y, engine.RenderingSystem);

        DebugStatsStyle style = new DebugStatsStyle
        {
            Font = engine.BuiltInAssets.Font_Default,
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
            Margin = new System.Numerics.Vector4(2, 2, 2, 2),
            Padding = new System.Numerics.Vector2(10, 4)
        };
        DebugStats.Initialize(_renderer, style);

        _resizeHandler = size => _renderer.SetResolution(size.X, size.Y);
        engine.MainPresenter.OnResize += _resizeHandler;
    }

    /// <inheritdoc />
    public override void OnUpdate(float deltaTime)
    {
        _renderer.Begin();
        DebugStats.ResetPosition();
    }

    /// <inheritdoc />
    public override void OnEndFrame(float deltaTime)
    {
        _renderer.End(_engine.MainPresenter);
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _engine.MainPresenter.OnResize -= _resizeHandler;
        DebugStats.Shutdown(_renderer);
        _renderer.Dispose();
    }
}
