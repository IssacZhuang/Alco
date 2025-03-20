using System.Numerics;
using Alco.Engine;
using Alco.Rendering;
using Alco;

using Alco.Graphics;
using Alco.GUI;



public class Game : GameEngine
{

    private readonly RenderContext _materialRenderer;
    private readonly Camera2D _camera;
    private readonly Material _material;
    private readonly Material _materialCompressed;
    private readonly Texture2D _texture;
    private readonly Texture2D _compressedTexture;
    private readonly ComputeMaterial _compressMaterial;
    private readonly TextureCompressorBC3 _compressor;
    private bool _isShowCompressed = false;
    public Game(GameEngineSetting setting) : base(setting)
    {
        _texture = Assets.Load<Texture2D>("test.jpg");
        
        _camera = Rendering.CreateCamera2D(MainWindow.Size, 1000);
        _materialRenderer = Rendering.CreateRenderContext();
        _material = Rendering.CreateGraphicsMaterial(BuiltInAssets.Shader_Sprite);
        _material.DepthStencilState = DepthStencilState.Default;
        _material.BlendState = BlendState.AlphaBlend;
        
        
       
        _compressMaterial = Rendering.CreateComputeMaterial(BuiltInAssets.Shader_TextureCompressBC3);
        //_compressMaterial.SetDefines("IS_SRGB");
        _compressor = Rendering.CreateTextureCompressorBC3(_compressMaterial);
        _compressor.IsSRGB = false;
        _compressedTexture = _compressor.Compress(_texture);


        _material.SetBuffer(ShaderResourceId.Camera, _camera);
        _material.SetTexture(ShaderResourceId.Texture, _texture);

        _materialCompressed = _material.CreateInstance();
        _materialCompressed.SetTexture(ShaderResourceId.Texture, _compressedTexture);
    }


    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }

        DebugGUI.Text(FrameRate);
        DebugGUI.CheckBoxWithText("Show Compressed", ref _isShowCompressed);


        Transform2D transform = Transform2D.Identity;
        transform.Scale = new Vector2(_texture.Width, _texture.Height);


        SpriteConstant constant = new SpriteConstant
        {
            Model = transform.Matrix,
            Color = new ColorFloat(1, 1, 1, 1),
            UvRect = new Rect(0, 0, 1, 1)
        };

        //draw atlas texture
        _materialRenderer.Begin(MainRenderTarget.FrameBuffer);
        if (_isShowCompressed)
        {
            _materialRenderer.DrawWithConstant(Rendering.MeshCenteredSprite, _materialCompressed, constant);
        }
        else
        {
            _materialRenderer.DrawWithConstant(Rendering.MeshCenteredSprite, _material, constant);
        }
        _materialRenderer.End();

    }

    protected override void OnStop()
    {
        _compressor.Dispose();
    }
}