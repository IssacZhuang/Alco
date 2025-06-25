using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Alco.Graphics;
using Alco.Rendering;
using Alco.Engine;
using Alco.ShaderCompiler;


using Alco;
using Alco.GUI;

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

    // resources for copmute shader
    private GraphicsValueBuffer<int> _iterationBuffer;
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

        _graphicsPipeline = CreatePipeline(GraphicsDevice.BindGroupUniformBuffer, GraphicsDevice.BindGroupTexture2DSampled);
        _computePipeline = CreateComputePipeline();
        _resourceGroupBuffer = CreateResourceGroup(GraphicsDevice.BindGroupUniformBuffer, _colorBuffer);

        _image = LaodTexture();
        _renderTarget = CreateRenderTarget(_image.Width, _image.Height);
        //_iterationBuffer = RenderingService.CreateTypedVRamBuffer<int>(8);
        _iterationBuffer = RenderingSystem.CreateGraphicsValueBuffer<int>(8, "iteration_buffer");

        //box blur texture


    }

    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }

        int value = _iterationBuffer.Value;
        _iterationBuffer.Value = value;
        _iterationBuffer.UpdateBuffer();
        _timer += delta;


        //_iterationBuffer.Value = 16;
        _commandBuffer.Begin();

        using (var computeScope = _commandBuffer.BeginCompute())
        {
            computeScope.SetComputePipeline(_computePipeline);
            computeScope.SetComputeResources(0, _image.EntryReadonly);
            computeScope.SetComputeResources(1, _renderTarget.EntryWriteable);
            computeScope.SetComputeResources(2, _iterationBuffer.EntryReadonly);
            computeScope.DispatchCompute(_image.Width / 8, _image.Height / 8, 1);
        }

        using (var renderScope = _commandBuffer.BeginRender(MainFrameBuffer))
        {
            renderScope.SetGraphicsPipeline(_graphicsPipeline);
            renderScope.SetVertexBuffer(0, _vertexBuffer);
            renderScope.SetIndexBuffer(_indexBuffer, IndexFormat.UInt16);
            renderScope.SetGraphicsResources(0, _resourceGroupBuffer);
            renderScope.SetGraphicsResources(1, _renderTarget.EntrySample);
            renderScope.DrawIndexed((uint)Indices.Length, 1, 0, 0, 0);
        }

        _commandBuffer.End();
        GraphicsDevice.Submit(_commandBuffer);
    }

    protected override void OnStop()
    {
        _vertexBuffer.Dispose();
        _indexBuffer.Dispose();
        _colorBuffer.Dispose();
        _graphicsPipeline.Dispose();
        _resourceGroupBuffer.Dispose();
        _image.Dispose();
        _renderTarget.Dispose();
        _iterationBuffer.Dispose();
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
        ShaderModule vertexShader = new ShaderModule(ShaderStage.Vertex, ShaderLanguage.WGSL, ShaderCode, "MainVS");
        ShaderModule fragmentShader = new ShaderModule(ShaderStage.Fragment, ShaderLanguage.WGSL, ShaderCode, "MainPS");

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
        DepthStencilState depthStencil = DepthStencilState.Default;

        GPUAttachmentLayout renderPass = MainRenderTarget.FrameBuffer.AttachmentLayout;

        GraphicsPipelineDescriptor pipelineDescriptor = new GraphicsPipelineDescriptor(
            new GPUBindGroup[] { bindGroupBuffer, bindGroupTexture },
            new ShaderModule[] { vertexShader, fragmentShader },
            new VertexInputLayout[] { vertexLayout },
            rasterizer,
            blend,
            depthStencil,
            new PixelFormat[] { renderPass.Colors[0].Format },
             renderPass.Depth.HasValue ? renderPass.Depth.Value.Format : null,
            null,
            "Quad Pipeline"
        );

        return GraphicsDevice.CreateGraphicsPipeline(pipelineDescriptor);
    }

    private GPUPipeline CreateComputePipeline()
    {
        string shaderCode = Encoding.UTF8.GetString(LoadFile("BoxBlur.hlsl"));

        //dxc
        ShaderModule computeShader = ShaderCompilerDxc.CrearteSpirvShaderModule(shaderCode, ShaderStage.Compute, "MainCS", "BoxBlur.hlsl");

        //shaderc hlsl
        //ShaderStageSource computeShader = ShaderCompilerShaderc.CrearteSpirvSourceFromHlsl(shaderCode, ShaderStage.Compute, "MainCS", "BoxBlur.hlsl");

        //shaderc glsl
        // string ShaderCode = Encoding.UTF8.GetString(LoadFile("BoxBlur.glsl"));
        // ShaderStageSource computeShader = ShaderCompilerShaderc.CrearteSpirvSourceFromGlsl(ShaderCode, ShaderStage.Compute, "main", "BoxBlur.glsl");


        // byte[] ShaderCode = LoadFile("BoxBlur.wgsl");
        // ShaderStageSource computeShader = new ShaderStageSource(ShaderStage.Compute, ShaderLanguage.WGSL, ShaderCode, "MainCS");

        DebugSaveFile("BoxBlur.spv", computeShader.Source);
        ShaderReflectionInfo reflectionInfo = UtilsShaderRelfection.GetSpirvReflection(computeShader.Source);
        Log.Info(reflectionInfo);
        

        ComputePipelineDescriptor pipelineDescriptor = new ComputePipelineDescriptor(
            computeShader,
            new GPUBindGroup[] { GraphicsDevice.BindGroupTexture2DRead, GraphicsDevice.BindGroupTexture2DStorage, GraphicsDevice.BindGroupUniformBuffer },
            null,
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

        return RenderingSystem.CreateTexture2DFromFile(data);
    }

    private Texture2D CreateRenderTarget(uint width, uint height)
    {
        return RenderingSystem.CreateTexture2D(width, height, new ColorFloat(1, 1, 1, 1), new ImageLoadOption
        {
            Format = PixelFormat.RGBA8Unorm,
            MipLevels = 1,
            Usage = TextureUsage.ComputeWrite,
            Name = "RenderTarget"
        });
    }

    private void UpdateColor(Vector3 color)
    {
        GraphicsDevice.WriteBuffer(_colorBuffer, 0, color);
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