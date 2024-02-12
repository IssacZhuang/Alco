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
        public Vector3 Position;
        public Vector3 Color;
        public Vector2 TexCoord;
    }

    private static readonly Vertex[] Vertices =
    {
        new Vertex {Position = new Vector3(-0.5f, 0.5f, 0.5f), Color = new Vector3(1.0f, 0.0f, 0.0f), TexCoord = new Vector2(0.0f, 0.0f)},
        new Vertex {Position = new Vector3(0.5f, 0.5f, 0.5f), Color = new Vector3(0.0f, 1.0f, 0.0f), TexCoord = new Vector2(1.0f, 0.0f)},
        new Vertex {Position = new Vector3(0.5f, -0.5f, 0.5f), Color = new Vector3(0.0f, 0.0f, 1.0f), TexCoord = new Vector2(1.0f, 1.0f)},
        new Vertex {Position = new Vector3(-0.5f, -0.5f, 0.5f), Color = new Vector3(1.0f, 1.0f, 1.0f), TexCoord = new Vector2(0.0f, 1.0f)}
    };

    private static readonly ushort[] Indices = { 0, 1, 2, 0, 2, 3 };

    #endregion

    private GPUCommandBuffer _commandBuffer;
    private GPUBuffer _vertexBuffer;
    private GPUBuffer _indexBuffer;
    private GPUBuffer _colorBuffer;
    private GPUPipeline _graphicsPipeline;
    private GPUPipeline _computePipeline;
    private GPUResourceGroup _resourceGroupBuffer;
    private Texture2D _image;
    private Texture2D _renderTarget;


    private float _timer = 0.0f;

    public Game(GameEngineSetting setting) : base(setting)
    {
        _commandBuffer = GraphicsDevice.CreateCommandBuffer();
        _vertexBuffer = CreateVertexBuffer();
        _indexBuffer = CreateIndexBuffer();

        _colorBuffer = GraphicsDevice.CreateBuffer(new BufferDescriptor
        {
            Name = "Color Buffer",
            Size = (uint)Marshal.SizeOf<Vector3>(),
            Usage = BufferUsage.Uniform | BufferUsage.CopyDst
        });

        UpdateColor(new Vector3(1, 1, 1));

        _graphicsPipeline = CreatePipeline(GraphicsDevice.BindGroupBuffer, GraphicsDevice.BindGroupTexture2DSampled);
        _computePipeline = CreateComputePipeline();
        _resourceGroupBuffer = CreateResourceGroup(GraphicsDevice.BindGroupBuffer, _colorBuffer);

        _image = LaodTexture();
        _renderTarget = CreateRenderTarget(_image.Width, _image.Hieght);

        //box blur texture
    }

    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(Key.Escape))
        {
            Stop();
        }
    }

    protected override void OnDraw(float delta)
    {
        _timer += delta;


        _commandBuffer.Begin();
        _commandBuffer.SetFrameBuffer(GraphicsDevice.SwapChainFrameBuffer);
        _commandBuffer.SetGraphicsPipeline(_graphicsPipeline);
        _commandBuffer.SetVertexBuffer(0, _vertexBuffer);
        _commandBuffer.SetIndexBuffer(_indexBuffer, IndexFormat.Uint16);
        _commandBuffer.SetGraphicsResources(0, _resourceGroupBuffer);
        _commandBuffer.SetGraphicsResources(1, _renderTarget.ResourcesSample);
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

    private GPUPipeline CreatePipeline(GPUBindGroup bindGroupBuffer, GPUBindGroup bindGroupTexture)
    {
        byte[] ShaderCode = LoadFile("DrawTexture.wgsl");
        ShaderStageSource vertexShader = new ShaderStageSource(ShaderStage.Vertex, ShaderLanguage.WGSL, ShaderCode, "vs_main");
        ShaderStageSource fragmentShader = new ShaderStageSource(ShaderStage.Fragment, ShaderLanguage.WGSL, ShaderCode, "fs_main");

        VertexInputLayout vertexLayout = new VertexInputLayout
        {
            StepMode = VertexStepMode.Vertex,
            Stride = (uint)Marshal.SizeOf<Vertex>(),
            Elements = new VertexElement[]
            {
                new VertexElement(0, 0, VertexFormat.Float32x3, "Position"),
                new VertexElement(1, (uint)Marshal.SizeOf<Vector3>(), VertexFormat.Float32x3, "Color"),
                new VertexElement(2, (uint)(Marshal.SizeOf<Vector3>() * 2), VertexFormat.Float32x2, "TexCoord"),
            }
        };

        RasterizerState rasterizer = RasterizerState.CullNone;
        BlendState blend = BlendState.NonPremultipliedAlpha;
        DepthStencilState depthStencil = DepthStencilState.DepthNone;

        GraphicsPipelineDescriptor pipelineDescriptor = new GraphicsPipelineDescriptor(
            new GPUBindGroup[] { bindGroupBuffer, bindGroupTexture },
            new ShaderStageSource[] { vertexShader, fragmentShader },
            new VertexInputLayout[] { vertexLayout },
            rasterizer,
            blend,
            depthStencil,
            new PixelFormat[] { GraphicsDevice.PrefferedSurfaceFomat },
            GraphicsDevice.PrefferedDepthStencilFormat,
            "Quad Pipeline"
        );

        return GraphicsDevice.CreateGraphicsPipeline(pipelineDescriptor);
    }

    private GPUPipeline CreateComputePipeline()
    {
        string shaderCode = Encoding.UTF8.GetString(LoadFile("BoxBlur.hlsl"));

        //dxc
        //ShaderStageSource computeShader = ShaderCompilerDxc.CrearteSpirvShaderSource(shaderCode, ShaderStage.Compute, "cs_main", "BoxBlur.hlsl");

        //shaderc hlsl
        //ShaderStageSource computeShader = ShaderCompilerShaderc.CrearteSpirvSourceFromHlsl(shaderCode, ShaderStage.Compute, "cs_main", "BoxBlur.hlsl");

        //shaderc glsl
        string ShaderCode = Encoding.UTF8.GetString(LoadFile("BoxBlur.glsl"));
        ShaderStageSource computeShader = ShaderCompilerShaderc.CrearteSpirvSourceFromGlsl(ShaderCode, ShaderStage.Compute, "main", "BoxBlur.glsl");


        // byte[] ShaderCode = LoadFile("BoxBlur.wgsl");
        // ShaderStageSource computeShader = new ShaderStageSource(ShaderStage.Compute, ShaderLanguage.WGSL, ShaderCode, "cs_main");

        ShaderReflectionInfo reflectionInfo = UtilsShaderRelfection.GetSpirvReflection(computeShader.Source);
        Log.Info(reflectionInfo);

        ComputePipelineDescriptor pipelineDescriptor = new ComputePipelineDescriptor(
            computeShader,
            new GPUBindGroup[] { GraphicsDevice.BindGroupTexture2DRead, GraphicsDevice.BindGroupStorageTexture2D },
            "box_blur_pipeline"
        );

        return GraphicsDevice.CreateComputePipeline(pipelineDescriptor);
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

    private Texture2D LaodTexture()
    {
        byte[] data = LoadFile("test.jpg");

        return GraphicsDevice.CreateTexture2DFromFile(data);
    }

    private Texture2D CreateRenderTarget(uint width, uint height)
    {
        return GraphicsDevice.CreateTexture2DEmpty(width, height, new Vector4(1, 1, 1, 1));
    }

    private void UpdateColor(Vector3 color)
    {
        GraphicsDevice.WriteBuffer(_colorBuffer, 0, color);
    }

    private static byte[] LoadFile(string path)
    {
        return File.ReadAllBytes(Path.Combine("Assets", path));
    }
}