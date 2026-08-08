using System.Numerics;
using Alco.Engine;
using Alco.Rendering;
using Alco;
using Alco.ImGUI;

using Random = Alco.FastRandom;
using Alco.Graphics;
using Alco.GUI;

public class Game : GameEngine
{
    //scence
    private readonly Camera2DBuffer _camera;

    private readonly Shader _shaderSprite;

    private readonly DropletSystem _dropletSystem;
    private readonly CubeSystem _cubeSystem;
    private readonly Texture2D _texDroplet;

    private readonly CollisionWorld2D _collisionWorld = new CollisionWorld2D();

    private readonly ForwardPipeline _mainPipeline;

    private Plane3D _plane;

    public ForwardPipeline MainPipeline => _mainPipeline;


    public Game(GameEngineSetting setting) : base(setting)
    {
        _mainPipeline = new ForwardPipeline(
            RenderingSystem,
            RenderingSystem.PreferredHDRPass,
            BuiltInAssets.Shader_Blit,
            MainView.Size.X,
            MainView.Size.Y);

        // The node chain: scene content first, then tone mapping.
        _mainPipeline.Use(new SceneNode(this));

        var tonemapNode = new RenderNode_Tonemap(
            RenderingSystem,
            BuiltInAssets.Shader_Blit,
            BuiltInAssets.Shader_ReinhardLuminanceTonemap,
            BuiltInAssets.Shader_Uncharted2Tonemap,
            BuiltInAssets.Shader_FilmicTonemap,
            BuiltInAssets.Shader_ACESTonemap,
            BuiltInAssets.Shader_NeutralTonemap,
            BuiltInAssets.Shader_AgXTonemap);
        _mainPipeline.Use(tonemapNode);

        MainPresenter.OnResize += size => _mainPipeline.Resize(size.X, size.Y);

        _shaderSprite = BuiltInAssets.Shader_Sprite;
        _texDroplet = AssetSystem.Load<Texture2D>("Droplet.png");

        _camera = RenderingSystem.CreateCamera2D(960, 540, 100f);

        _camera.UpdateMatrixToGPU();

        _plane = new Plane3D(new Vector3(0, 0, 1), 0);


        _dropletSystem = new DropletSystem(RenderingSystem, _camera, BuiltInAssets.Shader_SpriteInstanced, _texDroplet);
        Material cubeMaterial = RenderingSystem.CreateMaterial(_shaderSprite, "Sprite");
        cubeMaterial.SetBuffer(ShaderResourceId.Camera, _camera);
        _cubeSystem = new CubeSystem(RenderingSystem, cubeMaterial, RenderingSystem.TextureWhite);

        AddSystem(new ImGUISystem(this));
    }

    protected override void OnTick(float delta)
    {
        base.OnTick(delta);
        _dropletSystem.OnTick(delta);
        _cubeSystem.OnTick(delta);

        _collisionWorld.ClearAll();
        _dropletSystem.PushCollisionTarget(_collisionWorld);
        _collisionWorld.BuildTree();
        _cubeSystem.PerformCollision(_collisionWorld);
    }

    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }

        Ray3D cameraRay = CameraMathUtility.ScreenPointToRay2D(MainView.MousePosition, MainView.Size, _camera.Data.ViewProjectionMatrix, -100, 100);

        if (Input.IsMouseDown(Mouse.Right))
        {
            if (_plane.IntersectRay(cameraRay, out Vector3 hitPoint))
            {
                _cubeSystem.Spawn(hitPoint);
            }
        }

        _dropletSystem.OnUpdate(delta);

        _mainPipeline.Render(MainPresenter.FrameBuffer);
    }

    protected override void OnStop()
    {
        base.OnStop();
        _dropletSystem.Dispose();
        _cubeSystem.Dispose();
        _texDroplet.Dispose();
        _shaderSprite.Dispose();
        _collisionWorld.Dispose();
        _mainPipeline.Dispose();
    }

    /// <summary>
    /// Content node drawing droplets and cubes into the pipeline-assigned target.
    /// </summary>
    private sealed class SceneNode : IForwardRenderNode
    {
        private readonly Game _game;

        public SceneNode(Game game)
        {
            _game = game;
        }

        public void OnRenderForward(GPUFrameBuffer target, GPUAttachmentLayout layout)
        {
            _game._dropletSystem.OnRender(target, layout);
            _game._cubeSystem.OnRender(target);
        }
    }
}