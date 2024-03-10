using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Silk.NET.Input;
using Vocore.Graphics;
using Vocore.Engine;

public class Game : GameEngine
{
    #region Shader Data

    [StructLayout(LayoutKind.Sequential)]
    private struct Vertex
    {
        public Vector3 Position;
        public Vector3 Color;
    }

    private static readonly Vertex[] Vertices =
    {
        new Vertex {Position = new Vector3(-0.5f, 0.5f, 0.5f), Color = new Vector3(1.0f, 0.0f, 0.0f)},
        new Vertex {Position = new Vector3(0.5f, 0.5f, 0.5f), Color = new Vector3(0.0f, 1.0f, 0.0f)},
        new Vertex {Position = new Vector3(0.5f, -0.5f, 0.5f), Color = new Vector3(0.0f, 0.0f, 1.0f)},
        new Vertex {Position = new Vector3(-0.5f, -0.5f, 0.5f), Color = new Vector3(1.0f, 1.0f, 1.0f)}
    };

    private static readonly ushort[] Indices = { 0, 1, 2, 0, 2, 3 };

    #endregion

    private GPUCommandBuffer _commandBuffer;
    private GPUBuffer _vertexBuffer;
    private GPUBuffer _indexBuffer;
    private GPUBuffer _colorBuffer;
    private GPUPipeline _pipeline;
    private GPUResourceGroup _resourceGroup;


    private float _timer = 0.0f;

    public Game(GameEngineSetting setting) : base(setting)
    {
        _commandBuffer = GraphicsDevice.CreateCommandBuffer();
        _vertexBuffer = CreateVertexBuffer();
        _indexBuffer = CreateIndexBuffer();

        _colorBuffer = GraphicsDevice.CreateBuffer(new BufferDescriptor
        {
            Name = "Color Buffer",
            Size = (uint)Marshal.SizeOf<Vector4>(),
            Usage = BufferUsage.Uniform | BufferUsage.CopyDst
        });

        GPUBindGroup bindGroup = CreateBindGroup();

        _pipeline = CreatePipeline(bindGroup);
        _resourceGroup = CreateResourceGroup(bindGroup, _colorBuffer);
    }

    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(Key.Escape))
        {
            Stop();
        }

        _timer += delta;
        UpdateColor(new Vector4(MathF.Sin(_timer), MathF.Cos(_timer), 0.0f, 1.0f));

        _commandBuffer.Begin();
        _commandBuffer.SetFrameBuffer(GraphicsDevice.SwapChainFrameBuffer);
        _commandBuffer.SetGraphicsPipeline(_pipeline);
        _commandBuffer.SetVertexBuffer(0, _vertexBuffer);
        _commandBuffer.SetIndexBuffer(_indexBuffer, IndexFormat.Uint16);
        _commandBuffer.SetGraphicsResources(0, _resourceGroup);
        _commandBuffer.DrawIndexed((uint)Indices.Length, 1, 0, 0, 0);
        _commandBuffer.End();
        GraphicsDevice.Submit(_commandBuffer);
    }

    private GPUBuffer CreateIndexBuffer()
    {
        return GraphicsDevice.CreateBuffer(new BufferDescriptor
        {
            Name = "Quad Index Buffer",
            Size = (uint)Marshal.SizeOf<ushort>() * (uint)Indices.Length,
            Usage = BufferUsage.Index | BufferUsage.CopyDst
        }, Indices);
    }

    private GPUBuffer CreateVertexBuffer()
    {
        return GraphicsDevice.CreateBuffer(new BufferDescriptor
        {
            Name = "Quad Vertex Buffer",
            Size = (uint)Marshal.SizeOf<Vertex>() * (uint)Vertices.Length,
            Usage = BufferUsage.Vertex | BufferUsage.CopyDst
        }, Vertices);
    }

    private GPUBindGroup CreateBindGroup()
    {
        return GraphicsDevice.CreateBindGroup(new BindGroupDescriptor
        {
            Name = "color",
            Bindings = new BindGroupEntry[]
            {
                new BindGroupEntry(0, ShaderStage.Fragment, BindingType.UniformBuffer),
            }
        });
    }

    private GPUPipeline CreatePipeline(GPUBindGroup bindGroup)
    {
        string shaderCode = Encoding.UTF8.GetString(LoadFile("Shader.hlsl"));


        //dxc
        // ShaderStageSource vertexShader = ShaderCompilerDxc.CrearteSpirvShaderSource(shaderCode, ShaderStage.Vertex, "vs_main", "Shader.hlsl");
        // ShaderStageSource fragmentShader = ShaderCompilerDxc.CrearteSpirvShaderSource(shaderCode, ShaderStage.Fragment, "fs_main", "Shader.hlsl");

        //shaderc
        ShaderStageSource vertexShader = ShaderCompilerShaderc.CrearteSpirvSourceFromHlsl(shaderCode, ShaderStage.Vertex, "vs_main", "Shader.hlsl");
        ShaderStageSource fragmentShader = ShaderCompilerShaderc.CrearteSpirvSourceFromHlsl(shaderCode, ShaderStage.Fragment, "fs_main", "Shader.hlsl");

        VertexInputLayout vertexLayout = new VertexInputLayout
        {
            StepMode = VertexStepMode.Vertex,
            Stride = (uint)Marshal.SizeOf<Vertex>(),
            Elements = new VertexElement[]
            {
                new VertexElement(0, 0, VertexFormat.Float32x3, "Position"),
                new VertexElement(1, (uint)Marshal.SizeOf<Vector3>(), VertexFormat.Float32x3, "Color")
            }
        };

        RasterizerState rasterizer = RasterizerState.CullNone;
        BlendState blend = BlendState.Opaque;
        DepthStencilState depthStencil = DepthStencilState.Default;

        GraphicsPipelineDescriptor pipelineDescriptor = new GraphicsPipelineDescriptor(
            new GPUBindGroup[] { bindGroup },
            new ShaderStageSource[] { vertexShader, fragmentShader },
            new VertexInputLayout[] { vertexLayout },
            rasterizer,
            blend,
            depthStencil,
            new PixelFormat[] { GraphicsDevice.PrefferedSurfaceFomat },
            GraphicsDevice.PrefferedDepthStencilFormat,
            null,
            "quad_pipeline"
        );

        return GraphicsDevice.CreateGraphicsPipeline(pipelineDescriptor);
    }

    private GPUResourceGroup CreateResourceGroup(GPUBindGroup bindGroup, GPUBuffer buffer)
    {
        return GraphicsDevice.CreateResourceGroup(new ResourceGroupDescriptor
        {
            Layout = bindGroup,
            Resources = new ResourceBindingEntry[]
            {
                new ResourceBindingEntry(0, buffer),
            }
        });
    }

    private void UpdateColor(Vector4 color)
    {
        GraphicsDevice.WriteBuffer(_colorBuffer, 0, color);
    }

    private static byte[] LoadFile(string path)
    {
        return File.ReadAllBytes(Path.Combine("Assets", path));
    }
}