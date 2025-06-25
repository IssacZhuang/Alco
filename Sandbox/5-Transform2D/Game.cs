using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Alco.Graphics;
using Alco.Engine;
using Alco.Rendering;
using Alco.ShaderCompiler;
using Alco;

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
        new Vertex {Position = new Vector2(-0.5f, 0.5f), TexCoord = new Vector2(0, 0)},
        new Vertex {Position = new Vector2(0.5f, 0.5f), TexCoord = new Vector2(1, 0)},
        new Vertex {Position = new Vector2(0.5f, -0.5f), TexCoord = new Vector2(1, 1)},
        new Vertex {Position = new Vector2(-0.5f, -0.5f), TexCoord = new Vector2(0, 1)}
    };

    private static readonly ushort[] Indices = { 0, 1, 2, 0, 2, 3 };

    #endregion

    private CameraData2D camera;

    private GPUCommandBuffer _commandBuffer;
    private GPUBuffer _vertexBuffer;
    private GPUBuffer _indexBuffer;
    private GraphicsValueBuffer<Matrix4x4> _cameraBuffer;
    private GraphicsValueBuffer<Matrix4x4> _modelBuffer;
    private GPUPipeline _pipeline;
    private Texture2D _texBlue;
    private Texture2D _texRed;
    private Texture2D _texGreen;

    private Transform2D _transform1;
    private Transform2D _transform2;
    private Transform2D _transform3;

    private float _timer = 0.0f;

    private float _timeMove = 0.0f;

    public Game(GameEngineSetting setting) : base(setting)
    {
        _commandBuffer = GraphicsDevice.CreateCommandBuffer();
        _pipeline = CreatePipeline();

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

        _cameraBuffer = RenderingSystem.CreateGraphicsValueBuffer<Matrix4x4>("camera_buffer");
        _modelBuffer = RenderingSystem.CreateGraphicsValueBuffer<Matrix4x4>("model_buffer");

        _texBlue = RenderingSystem.CreateTexture2D(16, 16, new ColorFloat(0, 0, 1, 1));
        _texRed = RenderingSystem.CreateTexture2D(16, 16, new ColorFloat(1, 0, 0, 1));
        _texGreen = RenderingSystem.CreateTexture2D(16, 16, new ColorFloat(0, 1, 0, 1));

        camera = new CameraData2D();
        camera.Transform.Position = new Vector2(0, 2);
        camera.Size = new Vector2(16, 9);
        Log.Info(camera.ViewProjectionMatrix);

        _transform1 = Transform2D.Identity;
        _transform2 = Transform2D.Identity;
        _transform3 = Transform2D.Identity;

        _transform1.Position.X = -4;
        _transform2.Position.X = 0;
        _transform3.Position.X = 4;

    }

    protected override void OnUpdate(float delta)
    {
        _timer += delta;
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }

        if (Input.IsKeyDown(KeyCode.Space))
        {
            _timeMove = _timer;
        }


        float t = math.clamp((_timer - _timeMove) * 2, 0, 1);
        float movement = t * (1 - t) * 4;
        _transform1.Position.Y = movement;
        _transform2.Rotation = new Rotation2D(45 * movement);
        _transform3.Scale = new Vector2(1 + movement, 1 + movement);

        _cameraBuffer.UpdateBuffer(camera.ViewProjectionMatrix);
        _modelBuffer.UpdateBuffer(_transform1.Matrix);

        _timer += delta;

        //draw
        _commandBuffer.Begin();
        using (var renderScope = _commandBuffer.BeginRender(MainFrameBuffer))
        {
            renderScope.SetGraphicsPipeline(_pipeline);
            renderScope.SetVertexBuffer(0, _vertexBuffer);
            renderScope.SetIndexBuffer(_indexBuffer, IndexFormat.UInt16);
            renderScope.SetGraphicsResources(0, _cameraBuffer.EntryReadonly);
            renderScope.SetGraphicsResources(1, _modelBuffer.EntryReadonly);
            renderScope.SetGraphicsResources(2, _texGreen.EntrySample);
            renderScope.DrawIndexed((uint)Indices.Length, 1, 0, 0, 0);
        }
        _commandBuffer.End();
        GraphicsDevice.Submit(_commandBuffer);

        _modelBuffer.UpdateBuffer(_transform2.Matrix);
        _commandBuffer.Begin();
        using (var renderScope = _commandBuffer.BeginRender(MainFrameBuffer))
        {
            renderScope.SetGraphicsPipeline(_pipeline);
            renderScope.SetVertexBuffer(0, _vertexBuffer);
            renderScope.SetIndexBuffer(_indexBuffer, IndexFormat.UInt16);
            renderScope.SetGraphicsResources(0, _cameraBuffer.EntryReadonly);
            renderScope.SetGraphicsResources(1, _modelBuffer.EntryReadonly);
            renderScope.SetGraphicsResources(2, _texRed.EntrySample);
            renderScope.DrawIndexed((uint)Indices.Length, 1, 0, 0, 0);
        }
        _commandBuffer.End();
        GraphicsDevice.Submit(_commandBuffer);

        _modelBuffer.UpdateBuffer(_transform3.Matrix);
        _commandBuffer.Begin();
        using (var renderScope = _commandBuffer.BeginRender(MainFrameBuffer))
        {
            renderScope.SetGraphicsPipeline(_pipeline);
            renderScope.SetVertexBuffer(0, _vertexBuffer);
            renderScope.SetIndexBuffer(_indexBuffer, IndexFormat.UInt16);
            renderScope.SetGraphicsResources(0, _cameraBuffer.EntryReadonly);
            renderScope.SetGraphicsResources(1, _modelBuffer.EntryReadonly);
            renderScope.SetGraphicsResources(2, _texBlue.EntrySample);
            renderScope.DrawIndexed((uint)Indices.Length, 1, 0, 0, 0);
        }
        _commandBuffer.End();
        GraphicsDevice.Submit(_commandBuffer);
    }

    protected override void OnStop()
    {
        _commandBuffer.Dispose();
        _texBlue.Dispose();
        _texRed.Dispose();
        _texGreen.Dispose();
    }

    private GPUPipeline CreatePipeline()
    {
        //shaderc glsl
        // string shaderCode = Encoding.UTF8.GetString(LoadFile("Shader.glsl"));
        // ShaderStageSource vertSource = ShaderCompilerShaderc.CrearteSpirvSourceFromGlsl(shaderCode, ShaderStage.Vertex, "main", "Shader.glsl");
        // ShaderStageSource fragSource = ShaderCompilerShaderc.CrearteSpirvSourceFromGlsl(shaderCode, ShaderStage.Fragment, "main", "Shader.glsl");

        //shaderc hlsl
        // string shaderCode = Encoding.UTF8.GetString(LoadFile("Shader.hlsl"));
        // ShaderStageSource vertSource = ShaderCompilerShaderc.CrearteSpirvSourceFromHlsl(shaderCode, ShaderStage.Vertex, "MainVS", "Shader.hlsl");
        // ShaderStageSource fragSource = ShaderCompilerShaderc.CrearteSpirvSourceFromHlsl(shaderCode, ShaderStage.Fragment, "MainPS", "Shader.hlsl");

        //dxc hlsl
        string shaderCode = Encoding.UTF8.GetString(LoadFile("Shader.hlsl"));
        ShaderModule vertSource = ShaderCompilerDxc.CrearteSpirvShaderModule(shaderCode, ShaderStage.Vertex, "MainVS", "Shader.hlsl");
        ShaderModule fragSource = ShaderCompilerDxc.CrearteSpirvShaderModule(shaderCode, ShaderStage.Fragment, "MainPS", "Shader.hlsl");

        ShaderReflectionInfo info = UtilsShaderRelfection.GetSpirvReflection(vertSource.Source, fragSource.Source, true);

        Log.Info(info);

        DebugSaveFile("vertex.spv", vertSource.Source);
        DebugSaveFile("fragment.spv", fragSource.Source);

        GPUBindGroup[] bindGroups = new GPUBindGroup[info.BindGroups.Count];
        for (int i = 0; i < info.BindGroups.Count; i++)
        {
            bindGroups[i] = GraphicsDevice.CreateBindGroup(info.BindGroups[i].ToDescriptor());
        }

        RasterizerState rasterizer = RasterizerState.CullNone;
        BlendState blend = BlendState.NonPremultipliedAlpha;
        DepthStencilState depthStencil = DepthStencilState.Default;

        GPUAttachmentLayout renderPass = MainRenderTarget.FrameBuffer.AttachmentLayout;

        GraphicsPipelineDescriptor descriptor = new GraphicsPipelineDescriptor(
            bindGroups,
            new ShaderModule[] { vertSource, fragSource },
            info.VertexLayouts.ToArray(),
            rasterizer,
            blend,
            depthStencil,
            new PixelFormat[] { renderPass.Colors[0].Format },
            renderPass.Depth.HasValue ? renderPass.Depth.Value.Format : null,
            null,
            "quad_pipline"
        );

        return GraphicsDevice.CreateGraphicsPipeline(descriptor);
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