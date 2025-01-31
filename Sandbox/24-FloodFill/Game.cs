using System.Numerics;
using Alco.Engine;
using Alco.Rendering;
using Alco;
using Alco.Graphics;

public class Game : GameEngine
{
    private readonly RenderTexture _lightMap;
    private readonly MaterialRenderer _materialRenderer;
    private readonly Camera2D _camera;
    private readonly Material _material;
    private readonly ComputeDispatcher _computeClearTexture;
    private readonly ComputeDispatcher _computeFloodFill;
    public Game(GameEngineSetting setting) : base(setting)
    {
        _lightMap = Rendering.CreateRenderTexture(Rendering.PrefferedLightMapPass, 65, 65);


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

        //draw atlas texture
        _materialRenderer.Begin(MainRenderTarget.FrameBuffer);
        _materialRenderer.DrawWithConstant(Rendering.MeshCenteredSprite, _material, constant);
        _materialRenderer.End();

    }
}