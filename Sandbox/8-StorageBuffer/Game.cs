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
    private GraphicsBuffer<Matrix4x4> _cameraBuffer;
    private GraphicsBuffer<float> _timerBuffer;
    private GraphicsArrayBuffer<Vector4> _positionsBuffer;

    private GPUPipeline _graphicsPipeline;
    private GPUPipeline _computePipeline;
    private Texture2D _texWhite;

    private Transform2D _transform1;
    private float _timer = 0.0f;

    public Game(GameEngineSetting setting) : base(setting)
    {
        _commandBuffer = GraphicsDevice.CreateCommandBuffer();
        _graphicsPipeline = CreateGraphicsPipeline();
        _computePipeline = CreateComputePipeline();

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

        _texWhite = Texture2D.CreateEmpty(16, 16, new ColorFloat(1, 1, 1, 1));

        camera = new CameraData2D();
        camera.transform.position = new Vector2(0, 2);
        camera.Size = new Vector2(160, 90);
        Log.Info(camera.ViewProjectionMatrix);

        _positionsBuffer = new GraphicsArrayBuffer<Vector4>(500, "positions_buffer");
        _timerBuffer = new GraphicsBuffer<float>("timer_buffer");

        _transform1 = Transform2D.Identity;

        _transform1.position.X = -32;

    }

    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }

        _cameraBuffer.Value = camera.ViewProjectionMatrix;
        _timer += delta;
        _timerBuffer.Value = _timer;
        // use compute shader instead
        // for (int i = 0; i < 500; i++)
        // {
        //     _positionsBuffer[i] = new Vector4(i, (float)math.cos(_timer + i * 0.1f) * 5, 0, 1);
        // }

        _commandBuffer.Begin();

        _commandBuffer.SetComputePipeline(_computePipeline);
        _commandBuffer.SetComputeResources(0, _positionsBuffer.EntryReadWrite);
        _commandBuffer.SetComputeResources(1, _timerBuffer.EntryReadonly);
        _commandBuffer.DispatchCompute((500 / 8) + 1, 1, 1);

        _commandBuffer.SetFrameBuffer(GraphicsDevice.SwapChainFrameBuffer);
        _commandBuffer.SetGraphicsPipeline(_graphicsPipeline);

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

    private unsafe GPUPipeline CreateGraphicsPipeline()
    {
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

    private GPUPipeline CreateComputePipeline()
    {
        string shaderCode = Encoding.UTF8.GetString(LoadFile("ComputePosition.hlsl"));
        ShaderStageSource computeSource = ShaderCompilerDxc.CrearteSpirvShaderSource(shaderCode, ShaderStage.Compute, "cs_main", "ComputePosition.hlsl");

        ShaderReflectionInfo info = UtilsShaderRelfection.GetSpirvReflection(computeSource.Source, true);

        Log.Info(info);

        DebugSaveFile("compute.spv", computeSource.Source);

        GPUBindGroup[] bindGroups = new GPUBindGroup[info.BindGroups.Length];
        for (int i = 0; i < info.BindGroups.Length; i++)
        {
            bindGroups[i] = GraphicsDevice.CreateBindGroup(info.BindGroups[i].ToDescriptor("bind_group_" + i));
        }

        ComputePipelineDescriptor descriptor = new ComputePipelineDescriptor(
            computeSource,
            bindGroups,
            "compute_pipline"
        );

        return GraphicsDevice.CreateComputePipeline(descriptor);
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