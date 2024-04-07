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

        _cameraBuffer = new GraphicsValueBuffer<Matrix4x4>("camera_buffer");

        _texBlue = Rendering.CreateTexture2D(16, 16, 0x0000FF);
        _texRed = Rendering.CreateTexture2D(16, 16, 0xFF0000);
        _texGreen = Rendering.CreateTexture2D(16, 16, 0x00FF00);

        camera = new CameraData2D();
        camera.transform.position = new Vector2(0, 2);
        camera.Size = new Vector2(16, 9);
        Log.Info(camera.ViewProjectionMatrix);

        _transform1 = Transform2D.Identity;
        _transform2 = Transform2D.Identity;
        _transform3 = Transform2D.Identity;

        _transform1.position.X = -4;
        _transform2.position.X = 0;
        _transform3.position.X = 4;

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
        _transform1.position.Y = movement;
        _transform2.rotation = new Rotation2D(math.radians(45 * movement));
        _transform3.scale = new Vector2(1 + movement, 1 + movement);

        _cameraBuffer.UpdateBuffer(camera.ViewProjectionMatrix);
        _timer += delta;

        //draw
        _commandBuffer.Begin();
        _commandBuffer.SetFrameBuffer(GraphicsDevice.SwapChainFrameBuffer);
        _commandBuffer.SetGraphicsPipeline(_pipeline);
        _commandBuffer.SetVertexBuffer(0, _vertexBuffer);
        _commandBuffer.SetIndexBuffer(_indexBuffer, IndexFormat.Uint16);
        _commandBuffer.SetGraphicsResources(0, _cameraBuffer.EntryReadonly);

        _commandBuffer.SetGraphicsResources(1, _texGreen.EntrySample);
        _commandBuffer.PushConstants(ShaderStage.Vertex, _transform1.Matrix);
        _commandBuffer.DrawIndexed((uint)Indices.Length, 1, 0, 0, 0);

        _commandBuffer.SetGraphicsResources(1, _texRed.EntrySample);
        _commandBuffer.PushConstants(ShaderStage.Vertex, _transform2.Matrix);
        _commandBuffer.DrawIndexed((uint)Indices.Length, 1, 0, 0, 0);

        _commandBuffer.SetGraphicsResources(1, _texBlue.EntrySample);
        _commandBuffer.PushConstants(ShaderStage.Vertex, _transform3.Matrix);
        _commandBuffer.DrawIndexed((uint)Indices.Length, 1, 0, 0, 0);

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

    private unsafe GPUPipeline CreatePipeline()
    {
        //shaderc glsl
        // string shaderCode = Encoding.UTF8.GetString(LoadFile("Shader.glsl"));
        // ShaderStageSource vertSource = ShaderCompilerShaderc.CrearteSpirvSourceFromGlsl(shaderCode, ShaderStage.Vertex, "main", "Shader.glsl");
        // ShaderStageSource fragSource = ShaderCompilerShaderc.CrearteSpirvSourceFromGlsl(shaderCode, ShaderStage.Fragment, "main", "Shader.glsl");

        //shaderc hlsl
        // string shaderCode = Encoding.UTF8.GetString(LoadFile("Shader.hlsl"));
        // ShaderStageSource vertSource = ShaderCompilerShaderc.CrearteSpirvSourceFromHlsl(shaderCode, ShaderStage.Vertex, "vs_main", "Shader.hlsl");
        // ShaderStageSource fragSource = ShaderCompilerShaderc.CrearteSpirvSourceFromHlsl(shaderCode, ShaderStage.Fragment, "fs_main", "Shader.hlsl");

        //dxc hlsl
        string shaderCode = Encoding.UTF8.GetString(LoadFile("Shader.hlsl"));
        ShaderStageSource vertSource = ShaderCompilerDxc.CrearteSpirvShaderSource(shaderCode, ShaderStage.Vertex, "vs_main", "Shader.hlsl");
        ShaderStageSource fragSource = ShaderCompilerDxc.CrearteSpirvShaderSource(shaderCode, ShaderStage.Fragment, "fs_main", "Shader.hlsl");

        ShaderReflectionInfo info = UtilsShaderRelfection.GetSpirvReflection(vertSource.Source, fragSource.Source, true);

        Log.Info(info);

        DebugSaveFile("vertex.spv", vertSource.Source);
        DebugSaveFile("fragment.spv", fragSource.Source);

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