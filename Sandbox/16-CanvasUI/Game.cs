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
    private UIText _label;
    private UISlider _slider;


    private float _alignHorizontal = TextAlign.Left;
    private float _alignVertical = TextAlign.Top;
    private float _lineSpacing = 1f;
    private float _fontSize = 16;
    private float _progress = 0f;
    private float _pivotX = 0f;


    public Game(GameEngineSetting setting) : base(setting)
    {
        _shaderSprite = Assets.Load<Shader>("Rendering/Shader/2D/Sprite-Masked.hlsl");
        _shaderText = Assets.Load<Shader>("Rendering/Shader/2D/Text-Masked.hlsl");
        _font = Assets.Load<Font>("Font/Default.ttf");

        CavanUIFactoryStyle style = new CavanUIFactoryStyle
        {
            Font = _font,
            FontSize = 16,
            TextColor = 0xffffff,
            SliderSize = new Vector2(140, 24),
            SliderHandleSize = new Vector2(16, 24),
            SliderColor = 0x2a2a2a,
            SliderHandleColor = 0x373737,
            SliderHandleHoverColor = 0x525252,
            SliderHandleDragColor = 0x234A6C,
            DefaultButtonText = "Button",
            ButtonSize = new Vector2(80, 24),
            ButtonTextColor = 0xffffff,
            ButtonColor = 0x2a2a2a,
            ButtonPressedColor = 0x234A6C,
            ButtonHoverColor = 0x3a3a3a,
            CheckBoxColor = 0x2a2a2a,
            CheckBoxHoverColor = 0x525252,
            CheckBoxPressedColor = 0x373737,
        };

        CanvasUIFactory factory = new CanvasUIFactory(style);

        UIInputTracker inputTracker = new UIInputTracker(Input, Window);
        _canvas = Rendering.CreateCanvas(_shaderSprite, _shaderText);
        _canvas.InputTracker = inputTracker;
        _canvas.Size = new Vector2(setting.Window.Width, setting.Window.Height);

        _root = new UINode
        {
            Name = "Root",
        };

        UISprite bg = new UISprite()
        {
            Size = new Vector2(100, 100),
            Color = 0x2c2c2c
        };

        UIText label = new UIText()
        {
            Font = _font,
            Position = new Vector2(0, 0),
            Size = new Vector2(100, 100),
            Color = 0xffffff,
            AlignHorizontal = TextAlign.Left,
            AlignVertical = TextAlign.Top,
            OverflowHorizontal = OverflowModeHorizontal.NextLine,
            OverflowVertical = OverflowModeVertical.Clamp,
            Text = "Hello World\naaaaaaaaaaa  aaaaaaaaaaaa\nbbbbbbbbbbbbbb\nccc",
        };


        _label = label;

        _root.Add(bg);
        _root.Add(label);

        UILayoutVertical layout = new UILayoutVertical()
        {
            Size = new Vector2(160, 100),
            PaddingTop = 4,
            PaddingBottom = 4,
            Spacing = 4,
            FitContentHeight = true,
            AlwaysUpdate = true,
        };

        UISprite bgLayout = new UISprite()
        {
            Size = new Vector2(160, 100),
            Color = 0xffffff,
            Anchor = Anchor.Stretch,
            IsLayoutAffected = false,
        };

        layout.Add(bgLayout);

        UIButton button = factory.CreateButton("Test");
        button.Position = new Vector2(0);
        layout.Add(button);

        UISlider slider = factory.CreateSlider();
        slider.Position = new Vector2(0);
        _slider = slider;
        layout.Add(slider);

        layout.Position = new Vector2(200, 0);

        _root.Add(layout);

        layout.UpdateLayout();
        layout.UpdateLayout();
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
        if (DebugGUI.SliderWithText("Align Horizontal", ref _alignHorizontal, -0.5f, 0.5f))
        {
            _label.AlignHorizontal = _alignHorizontal;
        }

        if (DebugGUI.SliderWithText("Align Vertical", ref _alignVertical, -0.5f, 0.5f))
        {
            _label.AlignVertical = _alignVertical;
        }

        if (DebugGUI.SliderWithText("Line Spacing", ref _lineSpacing, 0.5f, 2f))
        {
            _label.LineSpacing = _lineSpacing;
        }

        if (DebugGUI.SliderWithText("Font Size", ref _fontSize, 8, 32))
        {
            _label.FontSize = _fontSize;
        }

        if(DebugGUI.SliderWithText("Pivot X", ref _pivotX, -0.5f, 0.5f))
        {
            _label.Pivot = new Vector2(_pivotX, 0);
        }

        if (DebugGUI.SliderWithText("Progress", ref _progress, 0, 1))
        {
            _slider.Value = _progress;
        }
    }

    protected override void OnStop()
    {
        _canvas.Dispose();
    }
}