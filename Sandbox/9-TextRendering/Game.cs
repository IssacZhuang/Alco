using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Vocore.Graphics;
using Vocore.Engine;
using Vocore.Rendering;
using Vocore;

public class Game : GameEngine
{

    private Camera2D _camera;

    private GPUCommandBuffer _commandBuffer;
    private Shader _shader;
    private TextRenderer _renderer;

    private Transform2D _transform1;
    private Font _font;
    private float _fontSize = 16;


    public Game(GameEngineSetting setting) : base(setting)
    {
        _commandBuffer = GraphicsDevice.CreateCommandBuffer();
        Assets.AddFileSource(new DirectoryFileSource("Assets"));
        if(Assets.TryLoad("Rendering/Shader/UI/Text.hlsl", out Shader? shader))
        {
            _shader = shader;
        }
        else
        {
            throw new Exception("Unable to load shader");
        }

        if(Assets.TryLoad("Font.ttf", out Font? font))
        {
            _font = font;
        }
        else
        {
            throw new Exception("Unable to load font");
        }



        _camera = new Camera2D();

        _camera.Size = new Vector2(640, 360);

        _transform1 = new Transform2D(Vector2.Zero, Rotation2D.Identity, Vector2.One * 9);


        _renderer = new TextRenderer(GraphicsDevice, _shader.Pipeline, font);
        _renderer.Camera = _camera;
        Log.Info(_font.GetGlyph('l'));
        Log.Info(_font.GetGlyph('!'));

        GC.Collect();
        GC.WaitForFullGCComplete();
    }

    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }

        if (Input.IsKeyPressing(KeyCode.Up))
        {
            _fontSize += delta * 10;
        }

        if (Input.IsKeyPressing(KeyCode.Down))
        {
            _fontSize -= delta * 10;
        }

        Log.Info(FrameRate);
       // _renderer.DrawString(FrameRate.ToString(), _fontSize, new Vector2(-320, -180), TextAlign.Center, new Vector4(1, 1, 1, 1));
        int drawCall = 400;
        for (int i = 0; i < drawCall; i++)
        {
            _renderer.DrawString("Hello World !!!", _fontSize, new Vector2(0, 0), TextAlign.Center, new Vector4(1, 1, 1, 1));
        }
        // _renderer.DrawString("Hello World !!!", _fontSize, new Vector2(0, 0), TextAlign.Center, new Vector4(1, 1, 1, 1));
        // _renderer.DrawString("cn: 中文", _fontSize, new Vector2(0, _fontSize), TextAlign.Center, 0xff6666);
        // _renderer.DrawString("jp: こんにちは", _fontSize, new Vector2(0, _fontSize * 2), TextAlign.Center, new Vector4(1, 1, 1, 1));
        // _renderer.DrawString("kr: 안녕하세요", _fontSize, new Vector2(0, _fontSize * 3), TextAlign.Center, new Vector4(1, 1, 1, 1));
        // _renderer.DrawString("ru: Привет", _fontSize, new Vector2(0, _fontSize * 4), TextAlign.Center, new Vector4(1, 1, 1, 1));
        // _renderer.DrawString("gr: Γειά σας", _fontSize, new Vector2(0, _fontSize * 5), TextAlign.Center, new Vector4(1, 1, 1, 1));
    }

    protected override void OnStop()
    {
        _commandBuffer.Dispose();
        _renderer.Dispose();
    }
}