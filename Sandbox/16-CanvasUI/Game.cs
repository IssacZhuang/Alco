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

        UISprite sprite1 = new UISprite(_renderer);
        sprite1.Texture = Rendering.TextureWhite;
        sprite1.Size = new Vector2(100, 100);

        _sprite1 = sprite1;

        UISprite sprite2 = new UISprite(_renderer);

        sprite1.Add(sprite2);

        sprite2.Texture = Rendering.TextureWhite;
        sprite2.Color = new Vector4(1, 0, 0, 0.5f);
        sprite2.transform.position = new Vector2(0, 0);
        sprite2.anchor = Anchor.Stretch;
        sprite2.Size = new Vector2(80, 80);

        

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
        

        _sprite1.Size = new Vector2(_size, _size);
        _sprite1.transform.position = new Vector2(_posX, 0);
        _sprite1.transform.rotation = Rotation2D.FromDegree(_rotDeg);
        _sprite1.transform.scale = new Vector2(_scale, _scale);
    }

    protected override void OnStop()
    {
        _renderer.Dispose();
    }
}