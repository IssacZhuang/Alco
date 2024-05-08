using System.Numerics;
using Vocore.Engine;
using Vocore.Rendering;
using Vocore;
using Vocore.GUI;

public class Game : GameEngine
{

    private Camera2D _camera;
    private Shader _shader;
    private SpriteRenderer _renderer;

    private UISprite _root;


    public Game(GameEngineSetting setting) : base(setting)
    {
        _shader = Assets.Load<Shader>("Rendering/Shader/2D/Sprite.hlsl");
        _camera = Rendering.CreateCamera2D(640, 360, 100);
        _renderer = Rendering.CreateSpriteRenderer(_camera, _shader);

        _root = new UISprite(_renderer);
        _root.Texture = Rendering.TextureWhite;
    }

    protected override void OnUpdate(float delta)
    {

    }

    protected override void OnStop()
    {

    }
}