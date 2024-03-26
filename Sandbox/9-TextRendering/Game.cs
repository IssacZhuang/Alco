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
    private GPUPipeline _textPipeline;
    private TextRenderer _renderer;

    private Transform2D _transform1;
    private Font _fontAtlas;
    private float _fontSize = 16;


    public Game(GameEngineSetting setting) : base(setting)
    {
        _commandBuffer = GraphicsDevice.CreateCommandBuffer();
        _textPipeline = CreateTextPipeline();

        _camera = new Camera2D();

        _camera.Size = new Vector2(640, 360);

        _transform1 = new Transform2D(Vector2.Zero, Rotation2D.Identity, Vector2.One * 9);

        _fontAtlas = CreateFontAtlas();

        _renderer = new TextRenderer(GraphicsDevice, _textPipeline);
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

        _renderer.DrawString(_fontAtlas, "Hello World !!!", _fontSize, new Vector2(0, 0), TextAlign.Center, new Vector4(1, 1, 1, 1));
        // DrawString("cn: 中文", new Vector2(0, _fontSize), new Vector4(1, 1, 1, 1), _fontSize);
        // DrawString("jp: こんにちは", new Vector2(0, _fontSize * 2), new Vector4(1, 1, 1, 1), _fontSize);
        // DrawString("kr: 안녕하세요", new Vector2(0, _fontSize * 3), new Vector4(1, 1, 1, 1), _fontSize);
        // DrawString("ru: Привет", new Vector2(0, _fontSize * 4), new Vector4(1, 1, 1, 1), _fontSize);
        // DrawString("gr: Γειά σας", new Vector2(0, _fontSize * 5), new Vector4(1, 1, 1, 1), _fontSize);
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

    private GPUPipeline CreateTextPipeline()
    {
        string shaderCode = Encoding.UTF8.GetString(LoadFile("DrawText.hlsl"));
        ShaderStageSource vertSource = ShaderCompilerDxc.CrearteSpirvShaderSource(shaderCode, ShaderStage.Vertex, "vs_main", "Shader.hlsl");
        ShaderStageSource fragSource = ShaderCompilerDxc.CrearteSpirvShaderSource(shaderCode, ShaderStage.Fragment, "fs_main", "Shader.hlsl");

        ShaderReflectionInfo info = UtilsShaderRelfection.GetSpirvReflection(vertSource.Source, fragSource.Source, true);

        GPUBindGroup[] bindGroups = new GPUBindGroup[info.BindGroups.Length];
        for (int i = 0; i < info.BindGroups.Length; i++)
        {
            bindGroups[i] = GraphicsDevice.CreateBindGroup(info.BindGroups[i].ToDescriptor());
        }

        Log.Info(info);

        RasterizerState rasterizer = RasterizerState.CullNone;
        BlendState blend = BlendState.NonPremultipliedAlpha;
        DepthStencilState depthStencil = DepthStencilState.Default;

        GraphicsPipelineDescriptor descriptor = new GraphicsPipelineDescriptor(
            bindGroups,
            new ShaderStageSource[] { vertSource, fragSource },
            info.VertexLayouts,
            rasterizer,
            blend,
            depthStencil,
            new PixelFormat[] { GraphicsDevice.PrefferedSurfaceFomat },
            GraphicsDevice.PrefferedDepthStencilFormat,
            info.PushConstantsRanges,
            "text_pipline"
        );

        return GraphicsDevice.CreateGraphicsPipeline(descriptor);
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

    private static void DebugSaveFile(string path, byte[] data)
    {
        if (!Directory.Exists(".Debug"))
        {
            Directory.CreateDirectory(".Debug");
        }
        File.WriteAllBytes(Path.Combine(".Debug", path), data);
    }
}