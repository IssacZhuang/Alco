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
    private GraphicsValueBuffer<float> _timerBuffer;
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

        _cameraBuffer = RenderingSystem.CreateGraphicsValueBuffer<Matrix4x4>("camera_buffer");

        _texWhite = RenderingSystem.CreateTexture2D(16, 16, new ColorFloat(1, 1, 1, 1));

        camera = new CameraData2D();
        camera.Transform.Position = new Vector2(0, 2);
        camera.Size = new Vector2(160, 90);
        Log.Info(camera.ViewProjectionMatrix);

        _positionsBuffer = RenderingSystem.CreateGraphicsArrayBuffer<Vector4>(500, "positions_buffer");
        _timerBuffer = RenderingSystem.CreateGraphicsValueBuffer<float>("timer_buffer");

        _transform1 = Transform2D.Identity;

        _transform1.Position.X = -32;

    }

    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }

        _cameraBuffer.UpdateBuffer(camera.ViewProjectionMatrix);
        _timer += delta;
        _timerBuffer.UpdateBuffer(_timer);
        // use compute shader instead
        // for (int i = 0; i < 500; i++)
        // {
        //     _positionsBuffer[i] = new Vector4(i, (float)math.cos(_timer + i * 0.1f) * 5, 0, 1);
        // }

        _commandBuffer.Begin();

        using (var computeScope = _commandBuffer.BeginCompute())
        {
            computeScope.SetComputePipeline(_computePipeline);
            computeScope.SetComputeResources(0, _positionsBuffer.EntryReadWrite);
            computeScope.SetComputeResources(1, _timerBuffer.EntryReadonly);
            computeScope.DispatchCompute((500 / 8) + 1, 1, 1);
        }

        using (var renderScope = _commandBuffer.BeginRender(MainFrameBuffer))
        {
            renderScope.SetGraphicsPipeline(_graphicsPipeline);
            renderScope.SetVertexBuffer(0, _vertexBuffer);
            renderScope.SetIndexBuffer(_indexBuffer, IndexFormat.UInt16);
            renderScope.SetGraphicsResources(0, _cameraBuffer.EntryReadonly);
            renderScope.SetGraphicsResources(1, _texWhite.EntrySample);
            renderScope.SetGraphicsResources(2, _positionsBuffer.EntryReadonly);
            renderScope.PushGraphicsConstants(ShaderStage.Vertex, _transform1.Matrix);
            renderScope.DrawIndexed((uint)Indices.Length, 100, 0, 0, 0);
        }

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
        ShaderModule vertSource = ShaderCompilerDxc.CrearteSpirvShaderModule(shaderCode, ShaderStage.Vertex, "MainVS", "Shader.hlsl");
        ShaderModule fragSource = ShaderCompilerDxc.CrearteSpirvShaderModule(shaderCode, ShaderStage.Fragment, "MainPS", "Shader.hlsl");

        ShaderReflectionInfo info = UtilsShaderRelfection.GetSpirvReflection(vertSource.Source, fragSource.Source, true);

        Log.Info(info);

        DebugSaveFile("vertex.spv", vertSource.Source);
        DebugSaveFile("fragment.spv", fragSource.Source);

        GPUBindGroup[] bindGroups = new GPUBindGroup[info.BindGroups.Count];
        for (int i = 0; i < info.BindGroups.Count; i++)
        {
            bindGroups[i] = GraphicsDevice.CreateBindGroup(info.BindGroups[i].ToDescriptor("bind_group_" + i));
        }

        RasterizerState rasterizer = RasterizerState.CullNone;
        BlendState blend = BlendState.NonPremultipliedAlpha;
        DepthStencilState depthStencil = DepthStencilState.Default;

        GPURenderPass renderPass = MainRenderTarget.FrameBuffer.RenderPass;

        GraphicsPipelineDescriptor descriptor = new GraphicsPipelineDescriptor(
            bindGroups,
            new ShaderModule[] { vertSource, fragSource },
            info.VertexLayouts.ToArray(),
            rasterizer,
            blend,
            depthStencil,
            new PixelFormat[] { renderPass.Colors[0].Format },
            renderPass.Depth.HasValue ? renderPass.Depth.Value.Format : null,
            info.PushConstantsRanges.ToArray(),
            "quad_pipline"
        );

        return GraphicsDevice.CreateGraphicsPipeline(descriptor);
    }

    private GPUPipeline CreateComputePipeline()
    {
        string shaderCode = Encoding.UTF8.GetString(LoadFile("ComputePosition.hlsl"));
        ShaderModule computeSource = ShaderCompilerDxc.CrearteSpirvShaderModule(shaderCode, ShaderStage.Compute, "MainCS", "ComputePosition.hlsl");

        ShaderReflectionInfo info = UtilsShaderRelfection.GetSpirvReflection(computeSource.Source, true);

        Log.Info(info);

        DebugSaveFile("compute.spv", computeSource.Source);

        GPUBindGroup[] bindGroups = new GPUBindGroup[info.BindGroups.Count];
        for (int i = 0; i < info.BindGroups.Count; i++)
        {
            bindGroups[i] = GraphicsDevice.CreateBindGroup(info.BindGroups[i].ToDescriptor("bind_group_" + i));
        }

        ComputePipelineDescriptor descriptor = new ComputePipelineDescriptor(
            computeSource,
            bindGroups,
            null,
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