using System.Numerics;
using Alco.Engine;
using Alco.Rendering;
using Alco;

using Random = Alco.Random;
using Alco.Graphics;
using Alco.GUI;

public class Game : GameEngine
{
    //scence
    private readonly Camera2D _camera;

    private readonly Shader _shaderSprite;

    private readonly DropletSystem _dropletSystem;
    private readonly CubeSystem _cubeSystem;
    private readonly Texture2D _texDroplet;
    
    private readonly CollisionWorld2D _collisionWorld = new CollisionWorld2D();

    private Plane3D _plane;


    public Game(GameEngineSetting setting) : base(setting)
    {
        _shaderSprite = BuiltInAssets.Shader_Sprite;
        _texDroplet = Assets.Load<Texture2D>("Droplet.png");

        _camera = Rendering.CreateCamera2D(960, 540, 100f);

        _camera.UpdateMatrixToGPU();

        _plane = new Plane3D(new Vector3(0, 0, 1), 0);


        _dropletSystem = new DropletSystem(MainRenderTarget, Rendering, _camera, _shaderSprite, _texDroplet);
        Material cubeMaterial = Rendering.CreateGraphicsMaterial(_shaderSprite, "Sprite");
        cubeMaterial.SetBuffer(ShaderResourceId.Camera, _camera);
        _cubeSystem = new CubeSystem(Rendering, cubeMaterial, _texDroplet);
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

        Ray3D cameraRay = UtilsCameraMath.ScreenPointToRay2D(MainView.MousePosition, MainView.Size, _camera.Data.ViewProjectionMatrix, -100, 100);

        if (Input.IsMouseDown(Mouse.Right))
        {
            if (_plane.IntersectRay(cameraRay, out Vector3 hitPoint))
            {
                _cubeSystem.Spawn(hitPoint);
            }
        }

        DebugGUI.Text(FrameRate);

        _dropletSystem.OnUpdate(delta);
        _cubeSystem.OnUpdate(MainFrameBuffer, delta);

        
    }

    protected override void OnStop()
    {
        base.OnStop();
        _dropletSystem.Dispose();
        _texDroplet.Dispose();
        _shaderSprite.Dispose();
        _collisionWorld.Dispose();
    }
}