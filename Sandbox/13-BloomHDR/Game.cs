using System.Numerics;
using Vocore.Engine;
using Vocore.Rendering;
using Vocore;

using Random = Vocore.Random;
using Vocore.Graphics;

public class Game : GameEngine
{
    //bloom
    private readonly Shader _blitShader;
    private readonly Shader _clampShader;
    private readonly Shader _blurShader;
    private readonly Bloom _bloom;
    


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
        //bloom
        _clampShader = Assets.Load<Shader>("Rendering/Shader/PostProcess/Bloom/Clamp.hlsl");
        _blurShader = Assets.Load<Shader>("Rendering/Shader/PostProcess/Bloom/GuassionBlur5x5.hlsl");
        _blitShader = Assets.Load<Shader>("Rendering/Shader/PostProcess/Bloom/Blit.hlsl");
        _bloom = Rendering.CreateBloom(_blitShader, _clampShader, _blurShader, 32);
        _bloom.SetInput(Rendering.DefaultFrameBuffer);

        //scene
        _textShader = Assets.Load<Shader>("Rendering/Shader/2D/Text.hlsl");
        _spriteShader = Assets.Load<Shader>("Sprite.hlsl");
       
        _font = Assets.Load<Font>("Font/Default.ttf");
        _star = Rendering.CreateTexture2D(4,4, 0xffffff);

        _camera = Rendering.CreateCamera2D(640, 360, 100);

        _textRenderer = Rendering.CreateTextRenderer(_camera, _textShader);
        _spriteRenderer = Rendering.CreateSpriteRenderer(_camera, _spriteShader);

        float[] weights_3x3 = { 0.227027f, 0.316216f, 0.227027f };
        float[] weights_5x5 = { 0.07027f, 0.316216f, 0.227027f, 0.316216f, 0.07027f };

        float total_brightness_3x3 = 0f;
        float total_brightness_5x5 = 0f;

        for(int i=-1; i<=1; i++)
        {
            for(int j=-1; j<=1; j++)
            {
                total_brightness_3x3 += weights_3x3[i+1] * weights_3x3[j+1];
            }
        }

        for(int i=-2; i<=2; i++)
        {
            for(int j=-2; j<=2; j++)
            {
                total_brightness_5x5 += weights_5x5[i+2] * weights_5x5[j+2];
            }
        }

        Log.Info(total_brightness_5x5/total_brightness_3x3);
    }

    protected override void OnResize(int2 size)
    {
        _bloom.SetInput(Rendering.DefaultFrameBuffer);
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

        _spriteRenderer.Draw(_star, spritePosition, Rotation2D.Identity, Vector2.One * 32, new ColorFloat(4f, 1.5f, 1.5f, 1));

        _spriteRenderer.End();

        _textRenderer.Begin(Rendering.DefaultFrameBuffer);
        _textRenderer.DrawString(_font, FrameRate.ToString(), 16, new Vector2(-320, 180), Rotation2D.Identity, Pivot.LeftTop, new Vector4(1, 1, 1, 1));
        _textRenderer.End();

        _bloom.Blit(Rendering.DefaultFrameBuffer);
    }

    protected override void OnStop()
    {
        _textRenderer.Dispose();
    }
}