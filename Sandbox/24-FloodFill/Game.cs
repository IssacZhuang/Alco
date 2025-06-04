using System.Numerics;
using Alco.Engine;
using Alco.Rendering;
using Alco;
using Alco.Graphics;
using Alco.GUI;
using Alco.IO;
using SandboxUtils;

public class Game : GameEngine
{

    private readonly uint2 _size = new uint2(65, 65);
    private readonly RenderContext _materialRenderer;
    private readonly Camera2DBuffer _camera;
    private readonly Material _material;
    private readonly FloodFillLightMap _tileLightMap;


    private float _intensity = 1;

    private int _iterations = 32;
    public Game(GameEngineSetting setting) : base(setting)

    {
        Material blitMaterial = RenderingSystem.CreateMaterial(AssetSystem.Load<Shader>("InverserGamma.hlsl"));

        _camera = RenderingSystem.CreateCamera2D(MainView.Size, 1000);
        _materialRenderer = RenderingSystem.CreateRenderContext();
        _material = blitMaterial.CreateInstance();
        _material.SetBuffer(ShaderResourceId.Camera, _camera);


        ComputeMaterial computeMaterial = RenderingSystem.CreateComputeMaterial(BuiltInAssets.Shader_FloodFillLighting);

        _tileLightMap = RenderingSystem.CreateTileLightMap(computeMaterial, (int)_size.X, (int)_size.Y, "tile_light_map");


        _material.SetRenderTexture(ShaderResourceId.Texture, _tileLightMap.Texture);
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

    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }

        DebugGUI.Text(FrameRate);
        if (DebugGUI.SliderWithText("Iterations", ref _iterations, 0, 100))
        {
            _tileLightMap.Iteration = _iterations;
        }
        DebugGUI.SliderWithText("Intensity", ref _intensity, 0, 2);
        if(DebugGUI.Button("Reset")) {
            _tileLightMap.AttenuationCorner = 0.1f;
            _tileLightMap.AttenuationSide = 0.141414f;
        }

        float attenuationSide = _tileLightMap.AttenuationSide;
        if(DebugGUI.SliderWithText("Attenuation Side", ref attenuationSide, 0, 2)){
            _tileLightMap.AttenuationSide = attenuationSide;
        }




        float attenuationCorner = _tileLightMap.AttenuationCorner;
        if(DebugGUI.SliderWithText("Attenuation Corner", ref attenuationCorner, 0, 2)){
            _tileLightMap.AttenuationCorner = attenuationCorner;
        }


        _camera.ViewSize = MainView.Size;
        _camera.UpdateMatrixToGPU();

        Transform2D transform = Transform2D.Identity;
        float scale = MainView.Width / _tileLightMap.Width;
        scale = math.min(scale, MainView.Height / _tileLightMap.Height);
        transform.Scale = new Vector2(_tileLightMap.Width * scale, _tileLightMap.Height * scale);

        SpriteConstant constant = new SpriteConstant
        {
            Model = transform.Matrix,
            Color = new ColorFloat(1, 1, 1, 1),
            UvRect = new Rect(0, 0, 1, 1)
        };

        _tileLightMap.SetLight((int)_size.X / 2, (int)_size.Y / 2, new Half4(_intensity, _intensity, _intensity, 1));
        _tileLightMap.Render();

        //draw atlas texture
        _materialRenderer.Begin(MainRenderTarget.FrameBuffer);
        _materialRenderer.DrawWithConstant(RenderingSystem.MeshCenteredSprite, _material, constant);
        _materialRenderer.End();
    }

    protected override void OnStop()
    {
        _tileLightMap.Dispose();
    }
}