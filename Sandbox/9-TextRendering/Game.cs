using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Silk.NET.Input;
using Vocore.Graphics;
using Vocore.Engine;
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


    private static readonly Vertex[] Vertices =
    {
        new Vertex {Position = new Vector2(-1f, 1f), TexCoord = new Vector2(0, 0)},
        new Vertex {Position = new Vector2(1f, 1f), TexCoord = new Vector2(1, 0)},
        new Vertex {Position = new Vector2(1f, -1f), TexCoord = new Vector2(1, 1)},
        new Vertex {Position = new Vector2(-1f, -1f), TexCoord = new Vector2(0, 1)}
    };

    private static readonly ushort[] Indices = { 0, 1, 2, 0, 2, 3 };

    #endregion

    private Camera2D camera;

    private GPUCommandBuffer _commandBuffer;
    private GPUBuffer _vertexBuffer;
    private GPUBuffer _indexBuffer;
    private GraphicsBuffer<Matrix4x4> _cameraBuffer;
    private GraphicsArrayBuffer<TextData> _textDataBuffer;

    private GPUPipeline _texturePipeline;
    private GPUPipeline _textPipeline;
    private Texture2D _texBlue;

    private Transform2D _transform1;
    private FontAtlas _fontAtlas;


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

        _texBlue = Texture2D.CreateEmpty(16, 16, new Vector4(0, 0, 1, 1));

        camera = new Camera2D();

        camera.Size = new Vector2(640, 360);
        _cameraBuffer.Value = camera.ViewProjectionMatrix;

        _transform1 = new Transform2D(Vector2.Zero, Rotation2D.Identity, Vector2.One * 9);

        _fontAtlas = CreateFontAtlas();

        GC.Collect();
        GC.WaitForFullGCComplete();
    }

    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(Key.Escape))
        {
            Stop();
        }

        // if (Input.IsKeyPressing(Key.Up))
        // {
        //     _scale -= delta;
        // }

        // if (Input.IsKeyPressing(Key.Down))
        // {
        //     _scale += delta;
        // }


    }

    protected unsafe override void OnDraw(float delta)
    {
        _commandBuffer.Begin();
        _commandBuffer.SetFrameBuffer(GraphicsDevice.SwapChainFrameBuffer);
        _commandBuffer.ClearColor(new Vector4(0.1f, 0.2f, 0.3f, 1), 0);
        _commandBuffer.ClearDepthStencil(1, 0);
        _commandBuffer.End();
        GraphicsDevice.Submit(_commandBuffer);

        //draw font atlas
        // _commandBuffer.Begin();
        // _commandBuffer.SetFrameBuffer(GraphicsDevice.SwapChainFrameBuffer);
        // _commandBuffer.SetGraphicsPipeline(_texturePipeline);
        // _commandBuffer.SetVertexBuffer(0, _vertexBuffer);
        // _commandBuffer.SetIndexBuffer(_indexBuffer, IndexFormat.Uint16);
        // _commandBuffer.SetGraphicsResources(0, _cameraBuffer.EntryReadonly);

        // _commandBuffer.SetGraphicsResources(1, _fontAtlas.Texture.EntrySample);
        // _commandBuffer.PushConstants(ShaderStage.Vertex, _transform1.Matrix);
        // _commandBuffer.DrawIndexed((uint)Indices.Length, 1, 0, 0, 0);

        // _commandBuffer.End();
        // GraphicsDevice.Submit(_commandBuffer);

        DrawString("Hello World", new Vector2(0, 0), new Vector4(1, 1, 1, 1), 32);
    }



    protected override void OnStop()
    {
        _commandBuffer.Dispose();
        _texBlue.Dispose();
    }

    private void DrawString(string text, Vector2 position, Vector4 color, float size, float lineSpacing = 1.0f)
    {
        float x = position.X;
        float y = position.Y;

        Transform2D transform = new Transform2D(position, Rotation2D.Identity, Vector2.One * size);

        uint drawCount = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == ' ')
            {
                x += size * 0.5f;
                continue;
            }

            if (c == '\n' || c == '\r')
            {
                x = position.X;
                y += size * lineSpacing;
                continue;
            }

            drawCount++;

            GlyphInfo glyph = _fontAtlas.GetGlyph(c);

            TextData data = new TextData
            {
                UVRect = new Vector4
                {
                    X = glyph.Position.X,
                    Y = glyph.Position.Y,
                    Z = glyph.Size.X,
                    W = glyph.Size.Y
                },
                Color = color,
                Offset = new Vector4(x + glyph.Offset.X * size, y + glyph.Offset.Y * size, 0, 0)
            };

            _textDataBuffer[i] = data;

            x += glyph.Advance ;
            
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
        _commandBuffer.DrawIndexed((uint)Indices.Length, drawCount, 0, 0, 0);
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

    private static FontAtlas CreateFontAtlas()
    {
        byte[] fontFile = LoadFile("Font.ttf");
        using FontAtlasPacker packer = new FontAtlasPacker(8192, 8192);

        packer.Add(fontFile, 32, new int2[]{
            FontAtlasPacker.RangeBasicLatin,
            FontAtlasPacker.RangeLatin1Supplement,
            FontAtlasPacker.RangeLatinExtendedA,
            FontAtlasPacker.RangeCyrillic,
            FontAtlasPacker.RangeGreek,
            //japanese
            FontAtlasPacker.RangeHiragana,
            FontAtlasPacker.RangeKatakana,
            //chinese
            FontAtlasPacker.RangeCjkUnifiedIdeographs,
            FontAtlasPacker.RangeCjkSymbolsAndPunctuation,
            //korean
            FontAtlasPacker.RangeHangulSyllables,
            FontAtlasPacker.RangeHangulCompatibilityJamo,
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