using System.Numerics;
using Alco.Engine;
using Alco.Rendering;
using Alco;
using Alco.GUI;
using System.Diagnostics;
using Alco.Graphics;

public class Game : GameEngine
{

    private readonly Canvas _canvas;
    private readonly Font _font;

    private CanvasUIFactory _factory;

    private UINode _root;
    private UIInputBox _inputBox;
    private UISlider _slider;
    private UILayoutVertical _layout;


    private float _alignHorizontal = TextAlign.Left;
    private float _alignVertical = TextAlign.Top;
    private float _lineSpacing = 1f;
    private float _fontSize = 16;
    private float _labelScale = 1f;
    private float _progress = 0f;
    private float _pivotY = 0f;
    private float _angle = 0f;
    private int _itemCount = 0;


    public Game(GameEngineSetting setting) : base(setting)
    {
        _font = BuiltInAssets.Font_Default;

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

        _factory = new CanvasUIFactory(style);

        UIInputTracker inputTracker = new UIInputTracker(Input, MainView);

        Material defaultSpriteMaterial = RenderingSystem.CreateGraphicsMaterial(BuiltInAssets.Shader_Sprite);
        defaultSpriteMaterial.BlendState = BlendState.NonPremultipliedAlpha;
        Material defaultTextMaterial = RenderingSystem.CreateGraphicsMaterial(BuiltInAssets.Shader_Text);
        defaultTextMaterial.BlendState = BlendState.NonPremultipliedAlpha;


        _canvas = RenderingSystem.CreateCanvas(inputTracker, defaultSpriteMaterial, defaultTextMaterial);
        _canvas.Size = new Vector2(setting.View.Width, setting.View.Height);
        _canvas.DebugDrawColor = new Vector4(0, 1, 0, 1);

        _root = new UINode
        {
            Name = "Root",
        };

        UISprite bg = new UISprite()
        {
            Position = new Vector2(50, 0),
            Size = new Vector2(100, 100),
            Color = 0x2c2c2c
        };

        UIInputBox inputBox = new UIInputBox()
        {
            Font = _font,
            Position = new Vector2(50, 0),
            Size = new Vector2(100, 100),
            Color = 0xffffff,
            AlignHorizontal = TextAlign.Left,
            AlignVertical = TextAlign.Top,
            OverflowHorizontal = OverflowModeHorizontal.NextLine,
            OverflowVertical = OverflowModeVertical.Clamp,
            Text = "Hello World\naaaaaaaaaaa  aaaaaaaaaaaa\nbbbb",
        };


        _inputBox = inputBox;

        _root.Add(bg);
        _root.Add(inputBox);

        UIText label = new UIText()
        {
            Font = _font,
            Position = new Vector2(0, -140),
            Size = new Vector2(100, 100),
            Color = 0xffffff,
            AlignHorizontal = TextAlign.Left,
            AlignVertical = TextAlign.Top,
            OverflowHorizontal = OverflowModeHorizontal.NextLine,
            OverflowVertical = OverflowModeVertical.Clamp,
            Text = "Hello World\naaaaaaaaaaa  aaaaaaaaaaaa\nbbbbbbbbbbbbbb\nccc",
        };

        _root.Add(label);

        UILayoutVertical layout = new UILayoutVertical()
        {
            Position = new Vector2(200, 100),
            Size = new Vector2(200, 100),
            PaddingTop = 8,
            PaddingBottom = 8,
            Spacing = 4,
            FitContentHeight = true,
        };
        _layout = layout;

        UISprite bgLayout = new UISprite()
        {
            Position = new Vector2(200, 100),
            Size = new Vector2(100, 200),
            Color = 0xaaaaaa,
            Anchor = Anchor.Stretch,
        };

        UIMask mask = new UIMask()
        {
            Position = new Vector2(200, 100),
            Size = new Vector2(100, 200)
        };

        UIMask mask2 = new UIMask()
        {
            Position = new Vector2(200, 300),
            Size = new Vector2(100, 200)
        };

        UIScrollable scrollable = new UIScrollable()
        {
            Position = new Vector2(200, 100),
            Size = new Vector2(100, 200),
            ScrollMode = SrollMode.Vertical | SrollMode.Horizontal,
            //IsMaskEnabled = true,
        };


        scrollable.Add(bgLayout);
        scrollable.Add(layout);

        scrollable.Content = layout;

        mask.Add(scrollable);

        _root.Add(mask2);
        _root.Add(mask);
        

        UISlider slider = _factory.CreateSlider();
        slider.Position = new Vector2(200, -100);
        _slider = slider;
        _root.Add(slider);

        //duplicate button test
        UIButton button = _factory.CreateButton("Button 1");
        button.Size = new Vector2(60, 60);
        button.Position = new Vector2(-200, -100);

        UIButton button2 = _factory.CreateButton("Button 2");
        button2.Size = new Vector2(80, 80);
        button2.Position = new Vector2(-200, -100);

        UIButton button3 = _factory.CreateButton("Button 3");
        button3.Size = new Vector2(100, 100);
        button3.Position = new Vector2(-200, -100);

        _root.Add(button3);
        button3.Add(button2);
        button2.Add(button);
    }

    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }

        _canvas.Tick(_root, delta);
        _canvas.Update(MainFrameBuffer, _root, delta);


        DebugGUI.Text(FrameRate);
        if (DebugGUI.SliderWithText("Align Horizontal", ref _alignHorizontal, -0.5f, 0.5f))
        {
            _inputBox.AlignHorizontal = _alignHorizontal;
        }

        if (DebugGUI.SliderWithText("Align Vertical", ref _alignVertical, -0.5f, 0.5f))
        {
            _inputBox.AlignVertical = _alignVertical;
        }

        if (DebugGUI.SliderWithText("Line Spacing", ref _lineSpacing, 0.5f, 2f))
        {
            _inputBox.LineSpacing = _lineSpacing;
        }

        if (DebugGUI.SliderWithText("Font Size", ref _fontSize, 8, 32))
        {
            _inputBox.FontSize = _fontSize;
        }

        if (DebugGUI.SliderWithText("Label Scale", ref _labelScale, 0.5f, 2f))
        {
            _inputBox.Scale = new Vector2(_labelScale);
        }

        if (DebugGUI.SliderWithText("Angle", ref _angle, 0, 360))
        {
            _inputBox.Rotation = new Rotation2D(_angle);
        }

        if (DebugGUI.SliderWithText("Progress", ref _progress, 0, 1))
        {
            _slider.Value = _progress;
        }

        if (DebugGUI.SliderWithText("Pivot Y", ref _pivotY, -0.5f, 0.5f))
        {
            _layout.Pivot = new Vector2(0f, _pivotY);
        }

        if (DebugGUI.SliderWithText("Item Count", ref _itemCount, 0, 10))
        {
            _layout.RemoveAllChildren();
            for (int i = 0; i < _itemCount; i++)
            {
                int index = i;
                UIButton button = _factory.CreateButton("Button " + i);
                button.EventOnClick += (canvas, mousePosition) =>
                {
                    Log.Info("Button " + index   + " clicked");
                };
                _layout.Add(button, false);
            }

            _layout.UpdateLayout();
        }

        if (DebugGUI.Button("Test Async"))
        {
            TestAsync();
        }
    }

    private async void TestAsync()
    {
        await Task.Delay(1000);
        Log.Info("TestAsync 1");
        await Task.Delay(1000);
        Log.Info("TestAsync 2");
        await Task.Delay(1000);
        Log.Info("TestAsync 3");
    }

    protected override void OnStop()
    {
        _canvas.Dispose();
    }
}