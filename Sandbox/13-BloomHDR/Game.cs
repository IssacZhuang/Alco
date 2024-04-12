using System.Numerics;
using Vocore.Engine;
using Vocore.Rendering;
using Vocore;

using Random = Vocore.Random;
using Vocore.Graphics;

public class Game : GameEngine
{
    //scence
    private readonly Camera2D _camera;
    private readonly Font _font;
    private readonly Texture2D _star;
    private readonly Shader _textShader;
    private readonly Shader _spriteShader;
    private readonly TextRenderer _textRenderer;
    private readonly SpriteRenderer _spriteRenderer;
    private float _white = 0;


    public Game(GameEngineSetting setting) : base(setting)
    {

        //scene
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

        Vector2 normalizedMousePosition = Input.MousePosition / new Vector2(1280, 720);
        Vector2 spritePosition = normalizedMousePosition * new Vector2(640, 360) - new Vector2(320, 180);
        spritePosition.Y = -spritePosition.Y;

        _spriteRenderer.Begin(Rendering.DefaultFrameBuffer);
        //_spriteRenderer.Draw(_star, new Vector2(0, 0), Rotation2D.Identity, Vector2.One * 20, new Vector4(1, 1, 1, 1));

        _spriteRenderer.Draw(_star, spritePosition, Rotation2D.Identity, Vector2.One * 32, new ColorFloat(4f, 2f, 2f, 1));

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