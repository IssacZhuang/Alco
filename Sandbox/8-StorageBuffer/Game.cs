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

    private Vector4[] _positions = new Vector4[500];
    private GPUBuffer _positionsBuffer;
    private GPUResourceGroup _positionsResource;

    private GPUPipeline _pipeline;
    private Texture2D _texWhite;

    private Transform2D _transform1;

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

        _positionsBuffer = GraphicsDevice.CreateBuffer(new BufferDescriptor
        {
            Name = "Positions Buffer",
            Size = (uint)(Marshal.SizeOf<Vector4>() * 500),
            Usage = BufferUsage.Uniform | BufferUsage.CopyDst,
        });

        _positionsResource = GraphicsDevice.CreateResourceGroup(new ResourceGroupDescriptor
        {
            Layout = GraphicsDevice.BindGroupBuffer,
            Resources = new ResourceBindingEntry[]
            {
                new ResourceBindingEntry(0, _positionsBuffer),
            },
            Name = "positions"
        });

        _cameraBuffer = new GraphicsBuffer<Matrix4x4>("camera_buffer");

        _texWhite = RenderingService.CreateTexture2DEmpty(16, 16, new Vector4(1, 1, 1, 1));

        camera = new Camera2D();
        camera.transform.position = new Vector2(0, 2);
        camera.Size = new Vector2(16, 9);
        Log.Info(camera.ViewProjectionMatrix);

        _transform1 = Transform2D.Identity;

        _transform1.position.X = -16;

    }

    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(Key.Escape))
        {
            Stop();
        }

        _cameraBuffer.Value = camera.ViewProjectionMatrix;

        for (int i = 0; i < _positions.Length; i++)
        {
            _positions[i] = new Vector4(i, MathF.Cos(i * 0.3f) * 3, 0, 0);
        }
    }

    protected unsafe override void OnDraw(float delta)
    {

        fixed (Vector4* ptr = _positions)
        {
            GraphicsDevice.WriteBuffer(_positionsBuffer, 0, (byte*)ptr, (uint)(Marshal.SizeOf<Vector4>() * _positions.Length));
        }
        _commandBuffer.Begin();
        _commandBuffer.SetFrameBuffer(GraphicsDevice.SwapChainFrameBuffer);
        _commandBuffer.SetGraphicsPipeline(_pipeline);
        _commandBuffer.SetVertexBuffer(0, _vertexBuffer);
        _commandBuffer.SetIndexBuffer(_indexBuffer, IndexFormat.Uint16);
        _commandBuffer.SetGraphicsResources(0, _cameraBuffer.EntryReadonly);

        _commandBuffer.SetGraphicsResources(1, _texWhite.EntrySample);
        _commandBuffer.SetGraphicsResources(2, _positionsResource);
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

        // bindGroups[bindGroups.Length-1] = GraphicsDevice.CreateBindGroup(new BindGroupDescriptor{
        //    Name= "Positions",
        //    Bindings = new BindGroupEntry[]{
        //         new BindGroupEntry(0, ShaderStage.Vertex, BindingType.StorageBuffer)
        //    }
        // });

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