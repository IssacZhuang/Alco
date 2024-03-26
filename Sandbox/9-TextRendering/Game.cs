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
    private Font _fontAtlas;
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

        _camera = new Camera2D();

        _camera.Size = new Vector2(640, 360);

        _transform1 = new Transform2D(Vector2.Zero, Rotation2D.Identity, Vector2.One * 9);

        _fontAtlas = CreateFontAtlas();

        _renderer = new TextRenderer(GraphicsDevice, _shader.Pipeline);
        _renderer.Camera = _camera;
        Log.Info(_fontAtlas.GetGlyph('l'));
        Log.Info(_fontAtlas.GetGlyph('!'));

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

        _renderer.DrawString(_fontAtlas, FrameRate.ToString(), _fontSize, new Vector2(-310, 160), TextAlign.Center, new Vector4(1, 1, 1, 1));
        _renderer.DrawString(_fontAtlas, "Hello World !!!", _fontSize, new Vector2(0, 0), TextAlign.Center, new Vector4(1, 1, 1, 1));
        _renderer.DrawString(_fontAtlas, "cn: 中文", _fontSize, new Vector2(0, _fontSize), TextAlign.Center, new Vector4(1, 1, 1, 1));
        _renderer.DrawString(_fontAtlas, "jp: こんにちは", _fontSize, new Vector2(0, _fontSize * 2), TextAlign.Center, new Vector4(1, 1, 1, 1));
        _renderer.DrawString(_fontAtlas, "kr: 안녕하세요", _fontSize, new Vector2(0, _fontSize * 3), TextAlign.Center, new Vector4(1, 1, 1, 1));
        _renderer.DrawString(_fontAtlas, "ru: Привет", _fontSize, new Vector2(0, _fontSize * 4), TextAlign.Center, new Vector4(1, 1, 1, 1));
        _renderer.DrawString(_fontAtlas, "gr: Γειά σας", _fontSize, new Vector2(0, _fontSize * 5), TextAlign.Center, new Vector4(1, 1, 1, 1));
    }

    protected override void OnStop()
    {
        _commandBuffer.Dispose();
    }

    private static Font CreateFontAtlas()
    {
        byte[] fontFile = LoadFile("Font.ttf");
        using FontAtlasPacker packer = new FontAtlasPacker(8192, 8192);

        packer.Add(fontFile, 32, new int2[]{
            UtilsUnicode.RangeBasicLatin,
            UtilsUnicode.RangeLatin1Supplement,
            UtilsUnicode.RangeLatinExtendedA,
            UtilsUnicode.RangeCyrillic,
            UtilsUnicode.RangeGreek,
            //japanese
            UtilsUnicode.RangeHiragana,
            UtilsUnicode.RangeKatakana,
            //chinese
            UtilsUnicode.RangeCjkUnifiedIdeographs,
            UtilsUnicode.RangeCjkSymbolsAndPunctuation,
            //korean
            UtilsUnicode.RangeHangulSyllables,
            UtilsUnicode.RangeHangulCompatibilityJamo,
        });


        return packer.Build();
    }

    private static byte[] LoadFile(string path)
    {
        return File.ReadAllBytes(Path.Combine("Assets", path));
    }
}