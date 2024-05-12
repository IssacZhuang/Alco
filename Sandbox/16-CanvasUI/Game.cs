using System.Numerics;
using Vocore.Engine;
using Vocore.Rendering;
using Vocore;
using Vocore.GUI;
using System.Diagnostics;

public class Game : GameEngine
{

    private readonly Canvas _canvas;
    private readonly Shader _shaderSprite;
    private readonly Shader _shaderText;
    private readonly Font _font;

    private UINode _root;
    private UISprite _sprite1;
    private UISprite _sprite2;
    private UILabel _label;

    private float _posX = 0;
    private float _rotDeg1 = 0;
    private float _rotDeg2 = 0;
    private float _size = 100;
    private float _scale = 1;
    private float _pivotX = 0;
    private float _textPivotY = 0;


    public Game(GameEngineSetting setting) : base(setting)
    {
        _shaderSprite = Assets.Load<Shader>("Rendering/Shader/2D/Sprite-Masked.hlsl");
        _shaderText = Assets.Load<Shader>("Rendering/Shader/2D/Text-Masked.hlsl");
        _font = Assets.Load<Font>("Font/Default.ttf");

        UIInputTracker inputTracker = new UIInputTracker(Input);
        _canvas = Rendering.CreateCanvas(_shaderSprite, _shaderText, _font);
        _canvas.InputTracker = inputTracker;

        _root = new UINode
        {
            Name = "Root",
        };

        UISprite sprite1 = new UISprite
        {
            Name = "Sprite1",
            Texture = Rendering.TextureWhite,
            Size = new Vector2(100, 100),
            IsMaskEnabled = true,
        };

        _sprite1 = sprite1;

        UISprite sprite2 = new UISprite(){
            Name = "Sprite2",
            Size = new Vector2(50, 50),
            Color = new Vector4(0, 1, 0, 1f),
            Position = new Vector2(25, 25),
            Anchor = Anchor.Center,
        };
        
        _sprite2 = sprite2;
        
        sprite1.Add(sprite2);
        _root.Add(sprite1);

        UILabel label = new UILabel()
        {
            Text = "Hello World",
            Size = new Vector2(50, 50), // same as sprite2
        };
        _label = label;

        sprite2.Add(label, false);

        UIButton button = new UIButton()
        {
            Size = new Vector2(100, 50),
            Position = new Vector2(0, -25),
            Anchor =  Anchor.Center,
        };
        button.OnClickEvent += () =>
        {
            Log.Info("Button Clicked");
        };

        UISprite bgButton = new UISprite()
        {
            Texture = Rendering.TextureWhite,
            Color = new Vector4(0.5f, 0.5f, 0.5f, 1),
            Size = new Vector2(100, 50),
            Position = new Vector2(0, -25),
            Anchor = Anchor.Stretch,
        };

        UILabel labelButton = new UILabel()
        {
            Text = "Click Me",
            Size = new Vector2(100, 50),
            Position = new Vector2(0, -25),
            Anchor = Anchor.Stretch,
        };

    
        button.Add(bgButton);
        button.Add(labelButton);

        _sprite1.Add(button);


    }

    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }

        Log.Info(_sprite1.Mask);

        _canvas.Update(Rendering.DefaultFrameBuffer, _root, delta);

        DebugGUI.Text(_sprite2.WorldTransform.position.ToString());
        DebugGUI.Slider(-320, 320, ref _posX);
        DebugGUI.SameLine();
        DebugGUI.Text("Position X");
        DebugGUI.Slider(0, 360, ref _rotDeg1);
        DebugGUI.SameLine();
        DebugGUI.Text("Rotation Degree");

        DebugGUI.Slider(0, 200, ref _size);
        DebugGUI.SameLine();
        DebugGUI.Text("Size");
        DebugGUI.Slider(0, 2, ref _scale);
        DebugGUI.SameLine();
        DebugGUI.Text("Scale");
        DebugGUI.Slider(-0.5f, 0.5f, ref _pivotX);
        DebugGUI.SameLine();
        DebugGUI.Text("Pivot");
        DebugGUI.Slider(0, 360, ref _rotDeg2);
        DebugGUI.SameLine();
        DebugGUI.Text("Rotation Degree 2");

        DebugGUI.Slider(-0.5f, 0.5f, ref _textPivotY);
        DebugGUI.SameLine();
        DebugGUI.Text("Text Pivot Y");


        if (_sprite2.Parent == _root)
        {
            if (DebugGUI.Button("Set Parent"))
            {
                _sprite2.SetParent(_sprite1);
            }

        }
        else
        {
            if (DebugGUI.Button("Remove Parent"))
            {
                _sprite2.SetParent(_root);
            }
        }


        _sprite1.Size = new Vector2(_size, _size);
        _sprite1.Position = new Vector2(_posX, 0);
        _sprite1.Rotation = Rotation2D.FromDegree(_rotDeg1);
        _sprite1.Scale = new Vector2(_scale, _scale);

        _sprite2.Pivot = new Pivot(_pivotX, 0);
        _sprite2.Rotation = Rotation2D.FromDegree(_rotDeg2);

        _label.TextPivot = new Pivot(0, _textPivotY);
    }

    protected override void OnStop()
    {
        _canvas.Dispose();
    }
}