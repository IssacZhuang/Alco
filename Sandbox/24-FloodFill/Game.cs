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
    private struct Data {
        public float attenuationCenter;
        public float attenuationSide;
        public float attenuationCorner;
    }

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
    private readonly GraphicsValueBuffer<Data> _dataBuffer;

    private float _intensity = 1;

    private int _iterations = 32;
    public Game(GameEngineSetting setting) : base(setting)

    {
        _command = GraphicsDevice.CreateCommandBuffer();
        _lightMap1 = Rendering.CreateRenderTexture(Rendering.PrefferedLightMapPass, _size.x, _size.y, FilterMode.Linear, "light_map_1");
        _lightMap2 = Rendering.CreateRenderTexture(Rendering.PrefferedLightMapPass, _size.x, _size.y, FilterMode.Linear, "light_map_2");


        _lightMap = new DoubleBuffer<RenderTexture>(_lightMap1, _lightMap2);

        _lightMapCPU = new BitmapFloat16RGBA(_size.x, _size.y);

        //set center pixel to 1
        _lightMapCPU[(int)_size.x / 2, (int)_size.y / 2] = new Half4(_intensity, _intensity, _intensity, 1);

        Material blitMaterial = Rendering.CreateGraphicsMaterial(Assets.Load<Shader>("InverserGamma.hlsl"));

        _camera = Rendering.CreateCamera2D(MainWindow.Size, 1000);
        _materialRenderer = Rendering.CreateMaterialRenderer();
        _material = blitMaterial.CreateInstance();
        _material.SetBuffer(ShaderResourceId.Camera, _camera);
        _material.SetRenderTexture(ShaderResourceId.Texture, _lightMap1);

        _dataBuffer = Rendering.CreateGraphicsValueBuffer<Data>("data_buffer");
        _dataBuffer.Value.attenuationCenter = 0f;
        _dataBuffer.Value.attenuationSide = 0.1f;
        _dataBuffer.Value.attenuationCorner = 0.141414f;
        _dataBuffer.UpdateBuffer();




        Shader shaderClearTexture = BuiltInAssets.Shader_ClearTexture;
        _computeClearTexture = Rendering.CreateComputeDispatcher(shaderClearTexture);

        Shader shaderFloodFill = BuiltInAssets.Shader_TileLighting;
        _computeFloodFill = Rendering.CreateComputeDispatcher(shaderFloodFill);
        _computeFloodFill.SetBuffer(ShaderResourceId.Data, _dataBuffer);
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
            _dataBuffer.Value.attenuationCenter = 0f;
            _dataBuffer.Value.attenuationSide = 0f;
            _dataBuffer.Value.attenuationCorner = 0f;
            _dataBuffer.UpdateBuffer();

        }
        if(DebugGUI.SliderWithText("Attenuation Center", ref _dataBuffer.Value.attenuationCenter, 0, 2)){
            _dataBuffer.UpdateBuffer();
        }

        if(DebugGUI.SliderWithText("Attenuation Side", ref _dataBuffer.Value.attenuationSide, 0, 2)){
            _dataBuffer.UpdateBuffer();
        }



        if(DebugGUI.SliderWithText("Attenuation Corner", ref _dataBuffer.Value.attenuationCorner, 0, 2)){
            _dataBuffer.UpdateBuffer();
        }

        _camera.ViewSize = MainWindow.Size;
        _camera.UpdateMatrixToGPU();

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

        _lightMapCPU[(int)_size.x / 2, (int)_size.y / 2] = new Half4(_intensity, _intensity, _intensity, 1);
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