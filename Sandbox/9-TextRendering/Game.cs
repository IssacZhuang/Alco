using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Vocore.Graphics;
using Vocore.Engine;
using Vocore.Rendering;
using Vocore;

public class Game : GameEngine
{
    #region Shader Data

    [StructLayout(LayoutKind.Sequential)]
    private struct Vertex
    {
        public Vector2 Position;
        public Vector2 TexCoord;
    }


    // private static readonly Vertex[] Vertices =
    // {
    //     new Vertex {Position = new Vector2(-0.5f, 0.5f), TexCoord = new Vector2(0, 0)},
    //     new Vertex {Position = new Vector2(0.5f, 0.5f), TexCoord = new Vector2(1, 0)},
    //     new Vertex {Position = new Vector2(0.5f, -0.5f), TexCoord = new Vector2(1, 1)},
    //     new Vertex {Position = new Vector2(-0.5f, -0.5f), TexCoord = new Vector2(0, 1)}
    // };

    private static readonly Vertex[] Vertices =
    {
        new Vertex {Position = new Vector2(0, 0), TexCoord = new Vector2(0, 0)},
        new Vertex {Position = new Vector2(1, 0), TexCoord = new Vector2(1, 0)},
        new Vertex {Position = new Vector2(1, -1), TexCoord = new Vector2(1, 1)},
        new Vertex {Position = new Vector2(0, -1), TexCoord = new Vector2(0, 1)}
    };

    private static readonly ushort[] Indices = { 0, 1, 2, 0, 2, 3 };

    #endregion

    private Camera2D _camera;

    private GPUCommandBuffer _commandBuffer;
    private GPUBuffer _vertexBuffer;
    private GPUBuffer _indexBuffer;
    private GraphicsBuffer<Matrix4x4> _cameraBuffer;
    private GraphicsArrayBuffer<TextData> _textDataBuffer;

    private GPUPipeline _texturePipeline;
    private GPUPipeline _textPipeline;
    private Texture2D _texBlue;

    private Transform2D _transform1;
    private Font _fontAtlas;
    private float _fontSize = 16;


    public Game(GameEngineSetting setting) : base(setting)
    {
        _commandBuffer = GraphicsDevice.CreateCommandBuffer();
        _texturePipeline = CreateTexturePipeline();
        _textPipeline = CreateTextPipeline();

        _vertexBuffer = GraphicsDevice.CreateBuffer(new BufferDescriptor
        {
            Name = "Vertex Buffer",
            Size = (uint)(Marshal.SizeOf<Vertex>() * Vertices.Length),
            Usage = BufferUsage.Vertex | BufferUsage.CopyDst,
        }, Vertices);

        _indexBuffer = GraphicsDevice.CreateBuffer(new BufferDescriptor
        {
            Name = "Index Buffer",
            Size = (uint)(sizeof(ushort) * Indices.Length),
            Usage = BufferUsage.Index | BufferUsage.CopyDst,
        }, Indices);

        _cameraBuffer = new GraphicsBuffer<Matrix4x4>("camera_buffer");
        _textDataBuffer = new GraphicsArrayBuffer<TextData>(200, "text_data_buffer");

        _texBlue = Texture2D.CreateEmpty(16, 16, new ColorFloat(0, 0, 1, 1));

        _camera = new Camera2D();

        _camera.Size = new Vector2(640, 360);
        _cameraBuffer.Value = _camera.ViewProjectionMatrix;

        _transform1 = new Transform2D(Vector2.Zero, Rotation2D.Identity, Vector2.One * 9);

        _fontAtlas = CreateFontAtlas();
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

        DrawString("Hello World !!!", new Vector2(0, 0), new Vector4(1, 1, 1, 1), _fontSize);
        DrawString("cn: 中文", new Vector2(0, _fontSize), new Vector4(1, 1, 1, 1), _fontSize);
        DrawString("jp: こんにちは", new Vector2(0, _fontSize * 2), new Vector4(1, 1, 1, 1), _fontSize);
        DrawString("kr: 안녕하세요", new Vector2(0, _fontSize * 3), new Vector4(1, 1, 1, 1), _fontSize);
        DrawString("ru: Привет", new Vector2(0, _fontSize * 4), new Vector4(1, 1, 1, 1), _fontSize);
        DrawString("gr: Γειά σας", new Vector2(0, _fontSize * 5), new Vector4(1, 1, 1, 1), _fontSize);
    }

    protected override void OnStop()
    {
        _commandBuffer.Dispose();
        _texBlue.Dispose();
    }

    private void DrawString(string text, Vector2 position, Vector4 color, float size, float lineSpacing = 1.0f)
    {
        float x = position.X / _camera.Size.X;
        float y = position.Y / _camera.Size.Y;

        Transform2D transform = new Transform2D(position, Rotation2D.Identity, Vector2.One * size);

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == ' ')
            {
                x += 0.5f;
                _textDataBuffer[i] = new TextData();
                continue;
            }

            if (c == '\n' || c == '\r')
            {
                x = position.X;
                y += lineSpacing;
                _textDataBuffer[i] = new TextData();
                continue;
            }



            GlyphInfo glyph = _fontAtlas.GetGlyph(c);

            TextData data = new TextData
            {
                UVRect = glyph.UVRect,
                Color = color,
                Offset = new Vector2(x, y) + glyph.Offset,
                Size = glyph.Size
            };

            _textDataBuffer[i] = data;

            x += glyph.Advance;

        }

        _commandBuffer.Begin();
        _commandBuffer.SetFrameBuffer(GraphicsDevice.SwapChainFrameBuffer);
        _commandBuffer.SetGraphicsPipeline(_textPipeline);
        _commandBuffer.SetVertexBuffer(0, _vertexBuffer);
        _commandBuffer.SetIndexBuffer(_indexBuffer, IndexFormat.Uint16);
        _commandBuffer.SetGraphicsResources(0, _cameraBuffer.EntryReadonly);
        _commandBuffer.SetGraphicsResources(1, _fontAtlas.Texture.EntrySample);
        _commandBuffer.SetGraphicsResources(2, _textDataBuffer.EntryReadonly);
        _commandBuffer.PushConstants(ShaderStage.Vertex, transform.Matrix);
        _commandBuffer.DrawIndexed((uint)Indices.Length, (uint)text.Length, 0, 0, 0);
        _commandBuffer.End();
        GraphicsDevice.Submit(_commandBuffer);
    }

    private unsafe GPUPipeline CreateTexturePipeline()
    {
        //dxc hlsl
        string shaderCode = Encoding.UTF8.GetString(LoadFile("DrawTexture.hlsl"));
        ShaderStageSource vertSource = ShaderCompilerDxc.CrearteSpirvShaderSource(shaderCode, ShaderStage.Vertex, "vs_main", "Shader.hlsl");
        ShaderStageSource fragSource = ShaderCompilerDxc.CrearteSpirvShaderSource(shaderCode, ShaderStage.Fragment, "fs_main", "Shader.hlsl");

        ShaderReflectionInfo info = UtilsShaderRelfection.GetSpirvReflection(vertSource.Source, fragSource.Source, true);

        GPUBindGroup[] bindGroups = new GPUBindGroup[info.BindGroups.Length];
        for (int i = 0; i < info.BindGroups.Length; i++)
        {
            bindGroups[i] = GraphicsDevice.CreateBindGroup(info.BindGroups[i].ToDescriptor());
        }

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
            "quad_pipline"
        );

        return GraphicsDevice.CreateGraphicsPipeline(descriptor);
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