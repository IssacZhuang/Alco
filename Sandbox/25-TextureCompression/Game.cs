using System.Numerics;
using Alco.Engine;
using Alco.Rendering;
using Alco;

using Alco.Graphics;
using Alco.GUI;

public class Game : GameEngine
{

    private readonly MaterialRenderer _materialRenderer;
    private readonly Camera2D _camera;
    private readonly Material _material;
    private readonly Texture2D _texture;
    private readonly Texture2D _compressedTexture;
    private readonly ComputeMaterial _compressMaterial;
    private readonly TextureCompressorBC3 _compressor;
    public Game(GameEngineSetting setting) : base(setting)
    {
        _texture = Assets.Load<Texture2D>("test.png");

        _camera = Rendering.CreateCamera2D(MainWindow.Size, 1000);
        _materialRenderer = Rendering.CreateMaterialRenderer();
        _material = Rendering.CreateGraphicsMaterial(BuiltInAssets.Shader_Sprite);
        _material.DepthStencilState = DepthStencilState.Default;
        _material.BlendState = BlendState.AlphaBlend;
        _material.SetBuffer(ShaderResourceId.Camera, _camera);
        _material.SetTexture(ShaderResourceId.Texture, _texture);

        _compressMaterial = Rendering.CreateComputeMaterial(BuiltInAssets.Shader_TextureCompressBC3);
        _compressor = Rendering.CreateTextureCompressorBC3(_compressMaterial);
        _compressedTexture = _compressor.Compress(_texture);
    }


    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }


        Transform2D transform = Transform2D.Identity;
        transform.scale = new Vector2(_texture.Width, _texture.Height);


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