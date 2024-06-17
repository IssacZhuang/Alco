using System.Numerics;
using Vocore.Engine;
using Vocore.Rendering;
using Vocore;
using Vocore.IO;

public class Game : GameEngine
{

    private Camera2D _camera;
    private Shader _shader;
    private TextRenderer _renderer;

    private Font _font;
    private float _fontSize = 16;
    private float _angle = 0;


    public Game(GameEngineSetting setting) : base(setting)
    {
        _shader = Assets.Load<Shader>("Rendering/Shader/2D/Text.hlsl");
        _font = Assets.Load<Font>("Font/Default.ttf");

        _camera = Rendering.CreateCamera2D(640, 360, 100);

        _renderer = Rendering.CreateTextRenderer(_camera, _shader);
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

        _angle += delta * 45;
        Rotation2D rotation = Rotation2D.FromDegree(_angle);

        _renderer.Begin(Rendering.DefaultFrameBuffer);
        _renderer.DrawString(_font, FrameRate.ToString(), _fontSize, new Vector2(-320, 180) , Rotation2D.Identity, Pivot.LeftTop, new Vector4(1, 1, 1, 1));

        _renderer.DrawString(_font, "Hello World !!!", _fontSize, new Vector2(0, 0), Rotation2D.Identity, Pivot.CenterBottom, new Vector4(1, 1, 1, 1));
        _renderer.DrawString(_font, "cn: 中文", _fontSize, new Vector2(0, _fontSize), Rotation2D.Identity, Pivot.LeftBottom, 0xff6666);
        _renderer.DrawString(_font, "jp: こんにちは", _fontSize, new Vector2(0, _fontSize * 2), Rotation2D.Identity, Pivot.CenterBottom, new Vector4(1, 1, 1, 1));
        _renderer.DrawString(_font, "kr: 안녕하세요", _fontSize, new Vector2(0, _fontSize * 3), Rotation2D.Identity, Pivot.CenterBottom, new Vector4(1, 1, 1, 1));
        _renderer.DrawString(_font, "ru: Привет", _fontSize, new Vector2(0, _fontSize * 4), Rotation2D.Identity, Pivot.RightBottom, new Vector4(1, 1, 1, 1));
        _renderer.DrawString(_font, "gr: Γειά σας", _fontSize, new Vector2(0, _fontSize * 5), Rotation2D.Identity, Pivot.RightBottom, new Vector4(1, 1, 1, 1));

        _renderer.DrawString(_font, "Rotation", _fontSize, new Vector2(-100, -100), rotation, Pivot.Center, new Vector4(1, 1, 1, 1));

        _renderer.DrawString(_font, "3D Text", _fontSize, new Vector3(0, -130f, 50), math.euler(0, math.radians(_angle), 0), Pivot.Center, new Vector4(1, 1, 1, 1));

        _renderer.End();
    }

    protected override void OnStop()
    {
        _renderer.Dispose();
    }
}