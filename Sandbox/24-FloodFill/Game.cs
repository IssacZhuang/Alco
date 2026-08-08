using System.Numerics;
using Alco.Engine;
using Alco.Rendering;
using Alco;
using Alco.Graphics;
using Alco.GUI;
using Alco.IO;
using SandboxUtils;
using Alco.ImGUI;

public class Game : GameEngine
{

    private readonly uint2 _size = new uint2(65, 65);
    private readonly RenderContext _materialRenderer;
    private readonly Camera2DBuffer _camera;
    private readonly Material _material;
    private readonly FloodFillLightMap _tileLightMap;
    private readonly GPUCommandBuffer _command;
    private readonly ForwardPipeline _mainPipeline;


    private float _intensity = 1;

    private int _iterations = 32;
    public Game(GameEngineSetting setting) : base(setting)

    {
        _mainPipeline = new ForwardPipeline(
            RenderingSystem,
            RenderingSystem.PreferredHDRPass,
            BuiltInAssets.Shader_Blit,
            MainView.Size.X,
            MainView.Size.Y);

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

        Material blitMaterial = RenderingSystem.CreateMaterial(AssetSystem.Load<Shader>("InverserGamma.hlsl"));

        _camera = RenderingSystem.CreateCamera2D(MainView.Size, 1000);
        _materialRenderer = RenderingSystem.CreateRenderContext();
        _material = blitMaterial.CreateInstance();
        _material.SetBuffer(ShaderResourceId.Camera, _camera);


        ComputeMaterial computeMaterial = RenderingSystem.CreateComputeMaterial(BuiltInAssets.Shader_FloodFillLighting);

        _tileLightMap = RenderingSystem.CreateTileLightMap(computeMaterial, (int)_size.X, (int)_size.Y, "tile_light_map");

        _material.SetRenderTexture(ShaderResourceId.Texture, _tileLightMap.Texture);

        _command = GraphicsDevice.CreateCommandBuffer();
        AddSystem(new ImGUISystem(this));
    }

    public override IEnumerable<IFileSource> CreateDefaultFileSources()
    {
        foreach (var fileSource in base.CreateDefaultFileSources())
        {
            yield return fileSource;
        }
        yield return new DirectoryWatcherFileSource(Utils.GetBuiltInAssetsPath(), AssetSystem);
        yield return new DirectoryWatcherFileSource(Utils.GetProjectAssetsPath(), AssetSystem);
    }

    protected override void OnEndFrame()
    {
        _mainPipeline.Render(MainPresenter.FrameBuffer);
    }

    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }

        // ImGUI Controls
        ImGui.Begin("Flood Fill Controls");


        float iterationsFloat = _iterations;
        if (ImGui.SliderFloat("Iterations", ref iterationsFloat, 0, 100))
        {
            _iterations = (int)iterationsFloat;
            _tileLightMap.Iteration = _iterations;
        }

        ImGui.SliderFloat("Intensity", ref _intensity, 0, 2);

        if (ImGui.Button("Reset"))
        {
            _tileLightMap.AttenuationCorner = 0.1f;
            _tileLightMap.AttenuationSide = 0.141414f;
        }

        float attenuationSide = _tileLightMap.AttenuationSide;
        if (ImGui.SliderFloat("Attenuation Side", ref attenuationSide, 0, 2))
        {
            _tileLightMap.AttenuationSide = attenuationSide;
        }

        float attenuationCorner = _tileLightMap.AttenuationCorner;
        if (ImGui.SliderFloat("Attenuation Corner", ref attenuationCorner, 0, 2))
        {
            _tileLightMap.AttenuationCorner = attenuationCorner;
        }

        ImGui.End();

        _camera.ViewSize = MainView.Size;
        _camera.UpdateMatrixToGPU();

        _tileLightMap.SetLight((int)_size.X / 2, (int)_size.Y / 2, new Half4(_intensity, _intensity, _intensity, 1));
        _tileLightMap.SetDirty();

        _command.Begin();
        using (var computePass = _command.BeginCompute())
        {
            _tileLightMap.Compute(computePass);
        }
        _command.End();
        GraphicsDevice.Submit(_command);
    }

    protected override void OnStop()
    {
        _tileLightMap.Dispose();
        _mainPipeline.Dispose();
    }

    private sealed class SceneNode : IForwardRenderNode
    {
        private readonly Game _game;

        public SceneNode(Game game)
        {
            _game = game;
        }

        public void OnRenderForward(GPUFrameBuffer target, GPUAttachmentLayout layout)
        {
            Transform2D transform = Transform2D.Identity;
            float scale = _game.MainView.Width / _game._tileLightMap.Width;
            scale = math.min(scale, _game.MainView.Height / _game._tileLightMap.Height);
            transform.Scale = new Vector2(_game._tileLightMap.Width * scale, _game._tileLightMap.Height * scale);

            SpriteConstant constant = new SpriteConstant
            {
                Model = transform.Matrix,
                Color = new ColorFloat(1, 1, 1, 1),
                UvRect = new Rect(0, 0, 1, 1)
            };

            //draw atlas texture
            _game._materialRenderer.Begin(target);
            _game._materialRenderer.DrawWithConstant(_game.RenderingSystem.MeshCenteredSprite, _game._material, constant);
            _game._materialRenderer.End();
        }
    }
}