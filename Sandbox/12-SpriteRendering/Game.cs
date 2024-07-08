using System.Numerics;
using Vocore.Engine;
using Vocore.Rendering;
using Vocore;

using Random = Vocore.Random;
using Vocore.Graphics;

public class Game : GameEngine
{
    private int DrawCount = 10000;
    private Camera2D _camera;
    private Font _font;
    private Shader _textShader;
    private Shader _spriteShader;
    private Texture2D _star;
    private TextRenderer _textRenderer;
    private SpriteRenderer _spriteRenderer;

    private Vector2[] _positions;
    private ColorFloat[] _colors;
    private Vector2 _size = Vector2.One * 20;


    public Game(GameEngineSetting setting) : base(setting)
    {
        _textShader = Assets.Load<Shader>("Rendering/Shader/2D/Text.hlsl");
        _spriteShader = Assets.Load<Shader>("Rendering/Shader/2D/Sprite.hlsl");
        _font = Assets.Load<Font>("Font/Default.ttf");
        _star = Assets.Load<Texture2D>("Star.png");

        _camera = Rendering.CreateCamera2D(640, 360, 100);

        _textRenderer = Rendering.CreateTextRenderer(_camera, _textShader);
        _spriteRenderer = Rendering.CreateSpriteRenderer(_camera, _spriteShader);


        _positions = new Vector2[DrawCount];
        _colors = new ColorFloat[DrawCount];

        //randomize positions

        Random random = new Random(123);
        for (int i = 0; i < DrawCount; i++)
        {
            _positions[i] = (random.NextVector2() - Vector2.One*0.5f) * new Vector2(640, 360);
            _colors[i] = random.NextUint();
        }
    }

    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }

        _textRenderer.Begin(MainFrameBuffer);
        _textRenderer.DrawString(_font, FrameRate.ToString(), 16, new Vector2(-320, 180), Rotation2D.Identity, Pivot.LeftTop, new Vector4(1, 1, 1, 1));
        _textRenderer.End();

        _spriteRenderer.Begin(MainFrameBuffer);
        //_spriteRenderer.Draw(_star, new Vector2(0, 0), Rotation2D.Identity, Vector2.One * 20, new Vector4(1, 1, 1, 1));

        for (int i = 0; i < DrawCount; i++)
        {
            _spriteRenderer.Draw(_star, _positions[i], Rotation2D.Identity, _size, _colors[i]);
        }

        _spriteRenderer.End();
    }

    protected override void OnStop()
    {
        _textRenderer.Dispose();
    }
}