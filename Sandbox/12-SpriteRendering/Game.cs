using System.Numerics;
using Alco.Engine;
using Alco.Rendering;
using Alco;

using Random = Alco.Random;
using Alco.Graphics;

public class Game : GameEngine
{
    private int DrawCount = 10000;
    private Camera2DBuffer _camera;
    private Material _materialText;
    private Material _materialSprite;
    private Font _font;
    private Texture2D _star;

    private RenderContext _renderContext;
    private TextRenderer _textRenderer;
    private SpriteRenderer _spriteRenderer;

    private Vector2[] _positions;
    private ColorFloat[] _colors;
    private Vector2 _size = Vector2.One * 20;


    public Game(GameEngineSetting setting) : base(setting)
    {
        _camera = RenderingSystem.CreateCamera2D(640, 360, 100);

        _materialText = RenderingSystem.CreateMaterial(BuiltInAssets.Shader_Text);
        _materialText.SetBuffer(ShaderResourceId.Camera, _camera);

        _materialSprite = RenderingSystem.CreateMaterial(BuiltInAssets.Shader_Sprite);
        _materialSprite.BlendState = BlendState.AlphaBlend;
        _materialSprite.SetBuffer(ShaderResourceId.Camera, _camera);

        _font = BuiltInAssets.Font_Default;
        _star = AssetSystem.Load<Texture2D>("Star.png");
    
        _renderContext = RenderingSystem.CreateRenderContext();
        _textRenderer = RenderingSystem.CreateTextRenderer(_renderContext, _materialText);
        _spriteRenderer = RenderingSystem.CreateSpriteRenderer(_renderContext, _materialSprite);


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

        // _textRenderer.Begin(MainFrameBuffer);
        // _textRenderer.DrawString(_font, FrameRate.ToString(), 16, new Vector2(-320, 180), Rotation2D.Identity, Pivot.LeftTop, new Vector4(1, 1, 1, 1));
        // _textRenderer.End();

        // _spriteRenderer.Begin(MainFrameBuffer);
        // //_spriteRenderer.Draw(_star, new Vector2(0, 0), Rotation2D.Identity, Vector2.One * 20, new Vector4(1, 1, 1, 1));

        // for (int i = 0; i < DrawCount; i++)
        // {
        //     _spriteRenderer.Draw(_star, _positions[i], Rotation2D.Identity, _size, _colors[i]);
        // }

        // _spriteRenderer.End();

        _renderContext.Begin(MainFrameBuffer);
        _textRenderer.DrawString(_font, FrameRate.ToString(), 16, new Vector2(-320, 180), Rotation2D.Identity, Pivot.LeftTop, new Vector4(1, 1, 1, 1));
        
        _spriteRenderer.Draw(_star, new Vector2(0, 0), Rotation2D.Identity, Vector2.One * 20, new Vector4(1, 1, 1, 1));

        for (int i = 0; i < DrawCount; i++)
        {
            _spriteRenderer.Draw(_star, _positions[i], Rotation2D.Identity, _size, _colors[i]);
        }
        
        _renderContext.End();
    }

    protected override void OnStop()
    {
        _textRenderer.Dispose();
    }
}