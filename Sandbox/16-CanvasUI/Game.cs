using System.Numerics;
using Vocore.Engine;
using Vocore.Rendering;
using Vocore;
using Vocore.GUI;
using System.Diagnostics;
using Vocore.Graphics;

public class Game : GameEngine
{

    private readonly Canvas _canvas;
    private readonly Shader _shaderSprite;
    private readonly Shader _shaderText;
    private readonly Font _font;

    private UINode _root;
    private UILabel _label;


    private float _alignHorizontal = TextAlign.Left;
    private float _alignVertical = TextAlign.Top;
    private float _lineSpacing = 1f;
    private float _fontSize = 16;


    public Game(GameEngineSetting setting) : base(setting)
    {
        _shaderSprite = Assets.Load<Shader>("Rendering/Shader/2D/Sprite-Masked.hlsl");
        _shaderText = Assets.Load<Shader>("Rendering/Shader/2D/Text-Masked.hlsl");
        _font = Assets.Load<Font>("Font/Default.ttf");

        UIInputTracker inputTracker = new UIInputTracker(Input, Window);
        _canvas = Rendering.CreateCanvas(_shaderSprite, _shaderText);
        _canvas.InputTracker = inputTracker;

        _root = new UINode
        {
            Name = "Root",
        };

        UISprite bg = new UISprite()
        {
            Size = new Vector2(100, 100),
            Color = 0xffffff
        };

        UILabel label = new UILabel()
        {
            Font = _font,
            Position = new Vector2(0, 0),
            Size = new Vector2(100, 100),
            Color = 0x000077,
            AlignHorizontal = TextAlign.Left,
            AlignVertical = TextAlign.Top,
            OverflowHorizontal = OverflowModeHorizontal.NextLine,
            OverflowVertical = OverflowModeVertical.Clamp,
            Text = "Hello World\naaaaaaaaaaa  aaaaaaaaaaaa\nbbbbbbbbbbbbbb\nccc",
        };

        _label = label;

        _root.Add(bg);
        _root.Add(label);
    }

    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }

        //Log.Info(_button.Mask, _button.MaskState);
        _canvas.Tick(_root, delta);
        _canvas.Update(Rendering.DefaultFrameBuffer, _root, delta);


        DebugGUI.Text(FrameRate);
        DebugGUI.SliderWithText("Align Horizontal", ref _alignHorizontal, -0.5f, 0.5f);
        DebugGUI.SliderWithText("Align Vertical", ref _alignVertical, -0.5f, 0.5f);
        DebugGUI.SliderWithText("Line Spacing", ref _lineSpacing, 0.5f, 2f);
        DebugGUI.SliderWithText("Font Size", ref _fontSize, 8, 32);

        _label.AlignHorizontal = _alignHorizontal;
        _label.AlignVertical = _alignVertical;
        _label.LineSpacing = _lineSpacing;
        _label.FontSize = _fontSize;

    }

    protected override void OnStop()
    {
        _canvas.Dispose();
    }
}