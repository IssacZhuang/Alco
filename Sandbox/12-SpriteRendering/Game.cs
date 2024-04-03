using System.Numerics;
using Vocore.Engine;
using Vocore.Rendering;
using Vocore;

public class Game : GameEngine
{

    private Camera2D _camera;
    private Font _font;
    private Shader _textShader;
    private TextRenderer _renderer;



    public Game(GameEngineSetting setting) : base(setting)
    {
        Assets.AddFileSource(new DirectoryFileSource("Assets"));

        _textShader = Assets.Load<Shader>("Rendering/Shader/2D/Text.hlsl");
        _font = Assets.Load<Font>("Font/Default.ttf");
        
        _camera = new Camera2D
        {
            Size = new Vector2(640, 360),
            Depth = 100
        };

        _renderer = new TextRenderer(_camera, _textShader);
    }

    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }

        _renderer.Begin(GraphicsDevice.SwapChainFrameBuffer);
        _renderer.DrawString(_font, FrameRate.ToString(), 16, new Vector2(-320, 180) , Rotation2D.Identity, Pivot.LeftTop, new Vector4(1, 1, 1, 1));
        _renderer.End();
    }

    protected override void OnStop()
    {
        _renderer.Dispose();
    }
}