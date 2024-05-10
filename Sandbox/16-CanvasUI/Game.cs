using System.Numerics;
using Vocore.Engine;
using Vocore.Rendering;
using Vocore;
using Vocore.GUI;
using System.Diagnostics;

public class Game : GameEngine
{

    private Camera2D _camera;
    private Shader _shader;
    private SpriteRenderer _renderer;

    private UIRoot _root;
    private UISprite _sprite1;
    private UISprite _sprite2;

    private float _posX = 0;
    private float _rotDeg = 0;
    private float _size = 100;
    private float _scale = 1;


    public Game(GameEngineSetting setting) : base(setting)
    {
        _shader = Assets.Load<Shader>("Rendering/Shader/2D/Sprite.hlsl");
        _camera = Rendering.CreateCamera2D(640, 360, 100);
        _renderer = Rendering.CreateSpriteRenderer(_camera, _shader);

        _root = new UIRoot();
        _root.Name = "Root";

        UISprite sprite1 = new UISprite(_renderer);
        sprite1.Name = "Sprite1";
        sprite1.Texture = Rendering.TextureWhite;
        sprite1.Size = new Vector2(100, 100);

        _sprite1 = sprite1;

        UISprite sprite2 = new UISprite(_renderer);
        _sprite2 = sprite2;

        

        sprite2.Texture = Rendering.TextureWhite;
        sprite2.Name = "Sprite2";
        sprite2.Color = new Vector4(1, 0, 0, 1f);
        sprite2.Position = new Vector2(0, 0);
        sprite2.Anchor = Anchor.LeftTop;
        sprite2.Size = new Vector2(80, 80);

        sprite1.Add(sprite2);
        _root.Add(sprite1);
    }

    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }

        _renderer.Begin(Rendering.DefaultFrameBuffer);
        _root.UpdateChild();
        _renderer.End();

        DebugGUI.Text(_sprite2.WorldTransform.position.ToString());
        DebugGUI.Slider(-320, 320, ref _posX);
        DebugGUI.SameLine();
        DebugGUI.Text("Position X");
        DebugGUI.Slider(0, 360, ref _rotDeg);
        DebugGUI.SameLine();
        DebugGUI.Text("Rotation Degree");

        DebugGUI.Slider(0, 200, ref _size);
        DebugGUI.SameLine();
        DebugGUI.Text("Size");
        DebugGUI.Slider(0, 2, ref _scale);
        DebugGUI.SameLine();
        DebugGUI.Text("Scale");

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
        _sprite1.Rotation = Rotation2D.FromDegree(_rotDeg);
        _sprite1.Scale = new Vector2(_scale, _scale);
    }

    protected override void OnStop()
    {
        _renderer.Dispose();
    }
}