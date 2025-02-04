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
    private readonly MaterialRenderer _materialRenderer;
    private readonly Camera2D _camera;
    private readonly Material _material;
    private readonly FloodFillLightMap _tileLightMap;


    private float _intensity = 1;

    private int _iterations = 32;
    public Game(GameEngineSetting setting) : base(setting)

    {
        Material blitMaterial = Rendering.CreateGraphicsMaterial(Assets.Load<Shader>("InverserGamma.hlsl"));

        _camera = Rendering.CreateCamera2D(MainWindow.Size, 1000);
        _materialRenderer = Rendering.CreateMaterialRenderer();
        _material = blitMaterial.CreateInstance();
        _material.SetBuffer(ShaderResourceId.Camera, _camera);


        ComputeMaterial computeMaterial = Rendering.CreateComputeMaterial(BuiltInAssets.Shader_FloodFillLighting);

        _tileLightMap = Rendering.CreateTileLightMap(computeMaterial, (int)_size.x, (int)_size.y, "tile_light_map");


        _material.SetRenderTexture(ShaderResourceId.Texture, _tileLightMap.Texture);
    }


    protected override void InitializeDefaultAssetLoader(GameEngineSetting setting)
    {
        base.InitializeDefaultAssetLoader(setting);
        DirectoryWatcherFileSource fileSource1 = new DirectoryWatcherFileSource(Utils.GetBuiltInAssetsPath(), Assets);
        Assets.AddFileSource(fileSource1);
        DirectoryWatcherFileSource fileSource2 = new DirectoryWatcherFileSource(Utils.GetProjectAssetsPath(), Assets);
        Assets.AddFileSource(fileSource2);
    }

    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }

        DebugGUI.Text(FrameRate);
        DebugGUI.SliderWithText("Iterations", ref _iterations, 0, 100);
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


        _camera.ViewSize = MainWindow.Size;
        _camera.UpdateMatrixToGPU();

        Transform2D transform = Transform2D.Identity;
        float scale = MainWindow.Width / _tileLightMap.Width;
        scale = math.min(scale, MainWindow.Height / _tileLightMap.Height);
        transform.scale = new Vector2(_tileLightMap.Width * scale, _tileLightMap.Height * scale);

        SpriteConstant constant = new SpriteConstant
        {
            Model = transform.Matrix,
            Color = new ColorFloat(1, 1, 1, 1),
            UvRect = new Rect(0, 0, 1, 1)
        };

        _tileLightMap.SetLight((int)_size.x / 2, (int)_size.y / 2, new Half4(_intensity, _intensity, _intensity, 1));
        _tileLightMap.Render();

        //draw atlas texture
        _materialRenderer.Begin(MainRenderTarget.FrameBuffer);
        _materialRenderer.DrawWithConstant(Rendering.MeshCenteredSprite, _material, constant);
        _materialRenderer.End();
    }
}