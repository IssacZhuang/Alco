using System.Numerics;
using Alco.Engine;
using Alco.Rendering;
using Alco;
using Alco.Graphics;

public class Game : GameEngine
{
    private readonly uint2 _size = new uint2(65, 65);
    private readonly RenderTexture _lightMap;
    private readonly BitmapFloat16RGBA _lightMapCPU;
    private readonly MaterialRenderer _materialRenderer;
    private readonly Camera2D _camera;
    private readonly Material _material;
    private readonly ComputeDispatcher _computeClearTexture;
    private readonly ComputeDispatcher _computeFloodFill;
    private readonly GPUCommandBuffer _command;
    public Game(GameEngineSetting setting) : base(setting)

    {
        _command = GraphicsDevice.CreateCommandBuffer();
        _lightMap = Rendering.CreateRenderTexture(Rendering.PrefferedLightMapPass, _size.x, _size.y);
        _lightMapCPU = new BitmapFloat16RGBA(_size.x, _size.y);
        // for (int i = 0; i < _lightMapCPU.Width; i++)
        // {
        //     for (int j = 0; j < _lightMapCPU.Height; j++)
        //     {
        //         _lightMapCPU[i, j] = new Half4(1, 1, 1, 1);
        //     }
        // }
        //set center pixel to 1
        _lightMapCPU[(int)_size.x / 2, (int)_size.y / 2] = new Half4(1, 1, 1, 1);

        



        Material blitMaterial = Rendering.CreateGraphicsMaterial(BuiltInAssets.Shader_Sprite);

        _camera = Rendering.CreateCamera2D(MainWindow.Size, 1000);
        _materialRenderer = Rendering.CreateMaterialRenderer();
        _material = blitMaterial.CreateInstance();
        _material.SetBuffer(ShaderResourceId.Camera, _camera);
        _material.SetRenderTexture(ShaderResourceId.Texture, _lightMap);

        Shader shaderClearTexture = BuiltInAssets.Shader_ClearTexture;
        _computeClearTexture = Rendering.CreateComputeDispatcher(shaderClearTexture);

        Shader shaderFloodFill = BuiltInAssets.Shader_TileLighting;
        _computeFloodFill = Rendering.CreateComputeDispatcher(shaderFloodFill);
        _computeFloodFill.SetRenderTexture(ShaderResourceId.Texture, _lightMap);
    }



    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }


        Transform2D transform = Transform2D.Identity;
        transform.scale = new Vector2(_lightMap.Width, _lightMap.Height);


        SpriteConstant constant = new SpriteConstant
        {
            Model = transform.Matrix,
            Color = new ColorFloat(1, 1, 1, 1),
            UvRect = new Rect(0, 0, 1, 1)
        };

        _lightMap.ColorTextures[0].SetPixels(_lightMapCPU);

        _command.Begin();
        _computeFloodFill.Dispatch(_command, 5, 5, 1);
        _command.End();
        GraphicsDevice.Submit(_command);


        //draw atlas texture
        _materialRenderer.Begin(MainRenderTarget.FrameBuffer);
        _materialRenderer.DrawWithConstant(Rendering.MeshCenteredSprite, _material, constant);
        _materialRenderer.End();

    }
}