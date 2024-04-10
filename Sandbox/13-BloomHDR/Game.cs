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
    private Shader _ToneMapShader;
    private Texture2D _star;
    private TextRenderer _textRenderer;
    private SpriteRenderer _spriteRenderer;
    private GPURenderPass _hdrPass;
    private ReinhardLuminanceToneMap _toneMap;
    private Vector2 _size = Vector2.One * 20;
    private float _white = 0;


    public Game(GameEngineSetting setting) : base(setting)
    {
        Assets.AddFileSource(new DirectoryFileSource("Assets"));

        _ToneMapShader = Assets.Load<Shader>("Rendering/Shader/ToneMap/ReinhardLuminanceTonemap.hlsl");
        _toneMap = Rendering.CreateReinhardLuminanceToneMap(_ToneMapShader);

        RenderPassDescriptor descriptor = new RenderPassDescriptor
        (
            [new(PixelFormat.RGBA16Float)],
            new(PixelFormat.Depth24PlusStencil8),
            "hdr_pass"
        );

        _hdrPass = GraphicsDevice.CreateRenderPass(descriptor);
        Rendering.SetMainRenderPass(_hdrPass, _toneMap);

        _textShader = Assets.Load<Shader>("Rendering/Shader/2D/Text.hlsl");
        _spriteShader = Assets.Load<Shader>("Sprite.hlsl");
       

        _font = Assets.Load<Font>("Font/Default.ttf");
        _star = Rendering.CreateTexture2D(4,4, 0xffffff);

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

        if (Input.IsKeyDown(KeyCode.Up))
        {
            _white += 0.1f;
            Log.Info(_white);
        }

        if (Input.IsKeyDown(KeyCode.Down))
        {
            _white -= 0.1f;
            Log.Info(_white);
        }

        if (Input.IsKeyDown(KeyCode.W))
        {
            _toneMap.Data.MaxLuminance += 1f;
            Log.Info(_toneMap.Data.MaxLuminance);
        }

        if (Input.IsKeyDown(KeyCode.S))
        {
            _toneMap.Data.MaxLuminance -= 1f;
            Log.Info(_toneMap.Data.MaxLuminance);
        }

        _spriteRenderer.Begin(Rendering.DefaultFrameBuffer);
        //_spriteRenderer.Draw(_star, new Vector2(0, 0), Rotation2D.Identity, Vector2.One * 20, new Vector4(1, 1, 1, 1));

        _spriteRenderer.Draw(_star, Vector2.Zero, Rotation2D.Identity, Vector2.One * 100, new ColorFloat(1, _white, _white, 1));

        _spriteRenderer.End();

        _textRenderer.Begin(Rendering.DefaultFrameBuffer);
        _textRenderer.DrawString(_font, FrameRate.ToString(), 16, new Vector2(-320, 180), Rotation2D.Identity, Pivot.LeftTop, new Vector4(1, 1, 1, 1));
        _textRenderer.End();
    }

    protected override void OnStop()
    {
        _textRenderer.Dispose();
    }
}