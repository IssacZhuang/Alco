using System.Numerics;
using Vocore.Engine;
using Vocore.Rendering;
using Vocore;

using Random = Vocore.Random;
using Vocore.Graphics;

public class Game : GameEngine
{
    private Camera2D _camera;
    private Font _font;
    private Shader _textShader;
    private Shader _spriteShader;
    private Shader _u2ToneMappingShader;
    private Texture2D _star;
    private TextRenderer _textRenderer;
    private SpriteRenderer _spriteRenderer;
    private GPURenderPass _hdrPass;
    private GPUFrameBuffer _hdrFrameBuffer;
    private Vector2 _size = Vector2.One * 20;


    public Game(GameEngineSetting setting) : base(setting)
    {
        Assets.AddFileSource(new DirectoryFileSource("Assets"));

        _hdrPass = Rendering.RegisterRenderPass("HDR", [PixelFormat.RGBA16Float], PixelFormat.Depth24PlusStencil8);
        FrameBufferDescriptor descriptor = new FrameBufferDescriptor
        {
            RenderPass = _hdrPass,
            Width = 640,
            Height = 360,
        };

        _hdrFrameBuffer = GraphicsDevice.CreateFrameBuffer(descriptor);

        _textShader = Assets.Load<Shader>("Rendering/Shader/2D/Text.hlsl");
        _spriteShader = Assets.Load<Shader>("Sprite.hlsl");
        _u2ToneMappingShader = Assets.Load<Shader>("Rendering/Shader/ToneMap/Uncharted2Tonemap.hlsl");

        _font = Assets.Load<Font>("Font/Default.ttf");
        _star = Assets.Load<Texture2D>("Star.png");

        _camera = Rendering.CreateCamera2D(640, 360, 100);

        _textRenderer = Rendering.CreateTextRenderer(_camera, _textShader);
        _spriteRenderer = Rendering.CreateSpriteRenderer(_camera, _spriteShader);

    }

    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }

        _textRenderer.Begin(GraphicsDevice.SwapChainFrameBuffer);
        _textRenderer.DrawString(_font, FrameRate.ToString(), 16, new Vector2(-320, 180), Rotation2D.Identity, Pivot.LeftTop, new Vector4(1, 1, 1, 1));
        _textRenderer.End();

        _spriteRenderer.Begin(_hdrFrameBuffer);
        //_spriteRenderer.Draw(_star, new Vector2(0, 0), Rotation2D.Identity, Vector2.One * 20, new Vector4(1, 1, 1, 1));

        _spriteRenderer.Draw(_star, Vector2.Zero, Rotation2D.Identity, Vector2.One*100, 0xffffff);

        _spriteRenderer.End();
    }

    protected override void OnStop()
    {
        _textRenderer.Dispose();
    }
}