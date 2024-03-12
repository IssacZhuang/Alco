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

    private Camera2D camera;

    private GPUCommandBuffer _commandBuffer;
    private GPUBuffer _vertexBuffer;
    private GPUBuffer _indexBuffer;
    private GraphicsBuffer<Matrix4x4> _cameraBuffer;
    private GraphicsArrayBuffer<Vector4> _positionsBuffer;

    private GPUPipeline _pipeline;
    private Texture2D _texWhite;

    private Transform2D _transform1;
    private float _timer = 0.0f;

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

        _cameraBuffer = new GraphicsBuffer<Matrix4x4>("camera_buffer");

        _texWhite = Texture2D.CreateEmpty(16, 16, new Vector4(1, 1, 1, 1));

        camera = new Camera2D();
        camera.transform.position = new Vector2(0, 2);
        camera.Size = new Vector2(16, 9);
        Log.Info(camera.ViewProjectionMatrix);

        _positionsBuffer = new GraphicsArrayBuffer<Vector4>(500, "positions_buffer");

        _transform1 = Transform2D.Identity;

        _transform1.position.X = -16;

    }

    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }

        _cameraBuffer.Value = camera.ViewProjectionMatrix;
        _timer += delta;
        for (int i = 0; i < 500; i++)
        {
            _positionsBuffer[i] = new Vector4(i, (float)math.cos(_timer + i * 0.1f) * 5, 0, 1);
        }

        //draw
        _commandBuffer.Begin();
        _commandBuffer.SetFrameBuffer(GraphicsDevice.SwapChainFrameBuffer);
        _commandBuffer.SetGraphicsPipeline(_pipeline);
        _commandBuffer.SetVertexBuffer(0, _vertexBuffer);
        _commandBuffer.SetIndexBuffer(_indexBuffer, IndexFormat.Uint16);
        _commandBuffer.SetGraphicsResources(0, _cameraBuffer.EntryReadonly);

        _commandBuffer.SetGraphicsResources(1, _texWhite.EntrySample);
        _commandBuffer.SetGraphicsResources(2, _positionsBuffer.EntryReadonly);
        _commandBuffer.PushConstants(ShaderStage.Vertex, _transform1.Matrix);
        _commandBuffer.DrawIndexed((uint)Indices.Length, 100, 0, 0, 0);

        _commandBuffer.End();
        GraphicsDevice.Submit(_commandBuffer);
    }


    protected override void OnStop()
    {
        _commandBuffer.Dispose();
        _texWhite.Dispose();
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
            bindGroups[i] = GraphicsDevice.CreateBindGroup(info.BindGroups[i].ToDescriptor("bind_group_" + i));
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