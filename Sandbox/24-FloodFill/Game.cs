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
    private readonly RenderTexture _lightMap1;
    private readonly RenderTexture _lightMap2;
    private readonly DoubleBuffer<RenderTexture> _lightMap;
    private readonly BitmapFloat16RGBA _lightMapCPU;
    private readonly MaterialRenderer _materialRenderer;
    private readonly Camera2D _camera;
    private readonly Material _material;
    private readonly ComputeDispatcher _computeClearTexture;
    private readonly ComputeDispatcher _computeFloodFill;

    private readonly GPUCommandBuffer _command;

    private int _iterations = 16;
    public Game(GameEngineSetting setting) : base(setting)

    {
        _command = GraphicsDevice.CreateCommandBuffer();
        _lightMap1 = Rendering.CreateRenderTexture(Rendering.PrefferedLightMapPass, _size.x, _size.y, "light_map_1");
        _lightMap2 = Rendering.CreateRenderTexture(Rendering.PrefferedLightMapPass, _size.x, _size.y, "light_map_2");


        _lightMap = new DoubleBuffer<RenderTexture>(_lightMap1, _lightMap2);

        _lightMapCPU = new BitmapFloat16RGBA(_size.x, _size.y);

        //set center pixel to 1
        _lightMapCPU[(int)_size.x / 2, (int)_size.y / 2] = new Half4(1, 1, 1, 1);

        Material blitMaterial = Rendering.CreateGraphicsMaterial(BuiltInAssets.Shader_Sprite);

        _camera = Rendering.CreateCamera2D(MainWindow.Size, 1000);
        _materialRenderer = Rendering.CreateMaterialRenderer();
        _material = blitMaterial.CreateInstance();
        _material.SetBuffer(ShaderResourceId.Camera, _camera);
        _material.SetRenderTexture(ShaderResourceId.Texture, _lightMap1);


        Shader shaderClearTexture = BuiltInAssets.Shader_ClearTexture;
        _computeClearTexture = Rendering.CreateComputeDispatcher(shaderClearTexture);

        Shader shaderFloodFill = BuiltInAssets.Shader_TileLighting;
        _computeFloodFill = Rendering.CreateComputeDispatcher(shaderFloodFill);
        
    }


    protected override void InitializeDefaultAssetLoader(GameEngineSetting setting)
    {
        base.InitializeDefaultAssetLoader(setting);
        DirectoryWatcherFileSource fileSource1 = new DirectoryWatcherFileSource(Utils.GetBuiltInAssetsPath(), Assets);
        Assets.AddFileSource(fileSource1);
    }

    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }

        DebugGUI.Text(FrameRate);
        DebugGUI.SliderWithText("Iterations", ref _iterations, 0, 100);

        Transform2D transform = Transform2D.Identity;
        float scale = MainWindow.Width / _lightMap1.Width;
        scale = math.min(scale, MainWindow.Height / _lightMap1.Height);
        transform.scale = new Vector2(_lightMap1.Width * scale, _lightMap1.Height * scale);

        SpriteConstant constant = new SpriteConstant
        {
            Model = transform.Matrix,
            Color = new ColorFloat(1, 1, 1, 1),
            UvRect = new Rect(0, 0, 1, 1)
        };

        _lightMap1.ColorTextures[0].SetPixels(_lightMapCPU);
        _lightMap.Reset();

        _command.Begin();
        for (int i = 0; i < _iterations; i++)
        {
            _computeFloodFill.SetRenderTexture(ShaderResourceId.FrontBuffer, _lightMap.Front);
            _computeFloodFill.SetRenderTexture(ShaderResourceId.BackBuffer, _lightMap.Back);
            _lightMap.Swap();
            _computeFloodFill.Dispatch(_command, 5, 5, 1);
        }
        _command.End();
        GraphicsDevice.Submit(_command);

        //draw atlas texture
        _materialRenderer.Begin(MainRenderTarget.FrameBuffer);
        _materialRenderer.DrawWithConstant(Rendering.MeshCenteredSprite, _material, constant);
        _materialRenderer.End();

    }
}