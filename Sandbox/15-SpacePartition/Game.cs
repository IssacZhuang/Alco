using System.Numerics;
using Vocore.Engine;
using Vocore.Rendering;
using Vocore;

using Random = Vocore.Random;
using Vocore.Graphics;
using Vocore.GUI;

public class Game : GameEngine
{
    //scence
    private readonly Camera2D _camera;

    private readonly Shader _shaderSprite;
    private readonly SpriteRenderer _spriteRenderer;
    private readonly DropletSystem _dropletSystem;
    private readonly CubeSystem _cubeSystem;
    private readonly Texture2D _texDroplet;
    
    private readonly CollisionWorld2D _collisionWorld = new CollisionWorld2D();

    private Plane3D _plane;


    public Game(GameEngineSetting setting) : base(setting)
    {
        _shaderSprite = Assets.Load<Shader>("Rendering/Shader/2D/Sprite.hlsl");
        _texDroplet = Assets.Load<Texture2D>("Droplet.png");

        _camera = Rendering.CreateCamera2D(960, 540, 100f);

        _camera.UpdateData();

        _plane = new Plane3D(new Vector3(0, 0, 1), 0);

        _spriteRenderer  = Rendering.CreateSpriteRenderer(_camera, _shaderSprite);

        _dropletSystem = new DropletSystem(_spriteRenderer, _texDroplet);
        _cubeSystem = new CubeSystem(_spriteRenderer, Rendering.TextureWhite);
    }

    protected override void OnTick(float delta)
    {
        base.OnTick(delta);
        _dropletSystem.OnTick(delta);
        _cubeSystem.OnTick(delta);

        _collisionWorld.ClearAll();
        _dropletSystem.PushCollisionTarget(_collisionWorld);
        _cubeSystem.PushCollisionCaster(_collisionWorld);
        _collisionWorld.BuildTree();
        _collisionWorld.Simulate();
    }

    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }

        Ray3D cameraRay = UtilsCameraMath.ScreenPointToRay2D(Input.MousePosition, Window.Size, _camera.Data.ViewProjectionMatrix, -100, 100);

        if(Input.IsMouseDown(Mouse.Right))
        {
            if (_plane.IntersectRay(cameraRay, out Vector3 hitPoint))
            {
                _cubeSystem.Spawn(hitPoint);
            }
        }

        DebugGUI.Text(FrameRate);

        _dropletSystem.OnUpdate(Rendering.DefaultFrameBuffer, delta);
        _cubeSystem.OnUpdate(Rendering.DefaultFrameBuffer, delta);

        
    }

    protected override void OnStop()
    {
        base.OnStop();
        _spriteRenderer.Dispose();
        _texDroplet.Dispose();
        _shaderSprite.Dispose();
        _collisionWorld.Dispose();
    }
}