using System.Numerics;
using Alco.Engine;
using Alco.Rendering;
using Alco;
using Alco.GUI;
using System.Diagnostics;
using Alco.Graphics;
using Alco.ImGUI;

public class Game : GameEngine
{

    private readonly Canvas _canvas;
    private readonly Font _font;

    private CanvasUIFactory _factory;

    private UINode _root;
    private UIInputBox _inputBox;
    private UISlider _slider;
    private UILayoutVertical _layout;

    private UISprite _sprite;


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

        Material defaultSpriteMaterial = RenderingSystem.CreateMaterial(BuiltInAssets.Shader_Sprite);
        defaultSpriteMaterial.BlendState = BlendState.NonPremultipliedAlpha;
        Material defaultTextMaterial = RenderingSystem.CreateMaterial(BuiltInAssets.Shader_Text);
        defaultTextMaterial.BlendState = BlendState.NonPremultipliedAlpha;


        _canvas = RenderingSystem.CreateCanvas(inputTracker, defaultSpriteMaterial, defaultTextMaterial);
        _canvas.Size = new Vector2(setting.View.Width, setting.View.Height);
        _canvas.DebugDrawColor = new Vector4(0, 1, 0, 1);

        _root = new UINode()
        {
            Name = "Root",
            Children = 
            {
                new UISprite()
                {
                    Position = new Vector2(50, 0),
                    Size = new Vector2(100, 100),
                    Color = 0x2c2c2c
                }
            }
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

        // _root.Add(bg);
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


        Texture2D texSelection = AssetSystem.Load<Texture2D>("Selection.png");
        _sprite = new UISprite()
        {
            Texture = texSelection,
            Size = new Vector2(100, 100),
            Position = new Vector2(0, 100),
            ImageType = ImageType.Sliced,
        };

        _root.Add(_sprite);
    }

    protected override void OnTick(float delta)
    {
        //just move this to OnUpdate if u want to update UI logic every frame
        _canvas.Tick(_root, delta);
    }

    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }

        DebugStats.Text(FrameRate);

        //_canvas.Tick(_root, delta);
        _canvas.Update(MainFrameBuffer, _root, delta);

        // ImGUI Controls
        ImGui.Begin("Canvas UI Controls");

        // Display frame rate
        FixedString32 framerateText = new FixedString32();
        framerateText.Append("Frame Rate: ");
        framerateText.Append(FrameRate);
        ImGui.Text(framerateText);

        if (ImGui.SliderFloat("Align Horizontal", ref _alignHorizontal, -0.5f, 0.5f))
        {
            _inputBox.AlignHorizontal = _alignHorizontal;
        }

        if (ImGui.SliderFloat("Align Vertical", ref _alignVertical, -0.5f, 0.5f))
        {
            _inputBox.AlignVertical = _alignVertical;
        }

        if (ImGui.SliderFloat("Line Spacing", ref _lineSpacing, 0.5f, 2f))
        {
            _inputBox.LineSpacing = _lineSpacing;
        }

        if (ImGui.SliderFloat("Font Size", ref _fontSize, 8, 32))
        {
            _inputBox.FontSize = _fontSize;
        }

        if (ImGui.SliderFloat("Label Scale", ref _labelScale, 0.5f, 2f))
        {
            _inputBox.Scale = new Vector2(_labelScale);
        }

        if (ImGui.SliderFloat("Angle", ref _angle, 0, 360))
        {
            _inputBox.Rotation = new Rotation2D(_angle);
        }

        if (ImGui.SliderFloat("Progress", ref _progress, 0, 1))
        {
            _slider.Value = _progress;
        }

        if (ImGui.SliderFloat("Pivot Y", ref _pivotY, -0.5f, 0.5f))
        {
            _layout.Pivot = new Vector2(0f, _pivotY);
        }

        float width = _sprite.Size.X;
        if (ImGui.SliderFloat("Sprite width", ref width, 0, 512))
        {
            _sprite.Size = new Vector2(width, _sprite.Size.Y);
        }

        float height = _sprite.Size.Y;
        if (ImGui.SliderFloat("Sprite height", ref height, 0, 512))
        {
            _sprite.Size = new Vector2(_sprite.Size.X, height);
        }

        float itemCountFloat = _itemCount;
        if (ImGui.SliderFloat("Item Count", ref itemCountFloat, 0, 10))
        {
            _itemCount = (int)itemCountFloat;
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

        if (ImGui.Button("Test Async"))
        {
            TestAsync();
        }

        ImGui.End();
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