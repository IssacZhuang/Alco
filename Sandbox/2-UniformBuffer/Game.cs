using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Alco.Graphics;
using Alco.Engine;
using Alco.ShaderCompiler;
using Alco;

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
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }

        _timer += delta;
        UpdateColor(new Vector4(MathF.Sin(_timer), MathF.Cos(_timer), 0.0f, 1.0f));

        _commandBuffer.Begin();
        using (var renderPass = _commandBuffer.BeginRender(MainFrameBuffer))
        {
            renderPass.SetPipeline(_pipeline);
            renderPass.SetVertexBuffer(0, _vertexBuffer);
            renderPass.SetIndexBuffer(_indexBuffer, IndexFormat.UInt16);
            renderPass.SetResources(0, _resourceGroup);
            renderPass.DrawIndexed((uint)Indices.Length, 1, 0, 0, 0);
        }
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
        string shaderCode = Encoding.UTF8.GetString(LoadFile("Shader.slang"));
        string appPath = Environment.CurrentDirectory;

        //dxc
        ShaderModule vertexShader = ShaderCompilerDxc.CrearteSpirvShaderModule(shaderCode, ShaderStage.Vertex, "MainVS", "Shader.hlsl");
        ShaderModule fragmentShader = ShaderCompilerDxc.CrearteSpirvShaderModule(shaderCode, ShaderStage.Fragment, "MainPS", "Shader.hlsl");
        string filePathVetex = Path.Combine(appPath, "spirv", "Shader.dxc.vert.spv");
        string filePathFragment = Path.Combine(appPath, "spirv", "Shader.dxc.frag.spv");

        //shaderc
        // ShaderStageSource vertexShader = ShaderCompilerShaderc.CrearteSpirvSourceFromHlsl(shaderCode, ShaderStage.Vertex, "MainVS", "Shader.hlsl");
        // ShaderStageSource fragmentShader = ShaderCompilerShaderc.CrearteSpirvSourceFromHlsl(shaderCode, ShaderStage.Fragment, "MainPS", "Shader.hlsl");

        //slang
        // ShaderModule[] shaderModules = ShaderCompilerSlang.CrearteSpirvShaderModules(shaderCode, "Shader.hlsl");
        // ShaderModule vertexShader = shaderModules[0];
        // ShaderModule fragmentShader = shaderModules[1];
        // string filePathVetex = Path.Combine(appPath, "spirv", "Shader.slang.vert.spv");
        // string filePathFragment = Path.Combine(appPath, "spirv", "Shader.slang.frag.spv");

        Log.Info(UtilsShaderRelfection.GetSpirvReflection(vertexShader.Source));

        if (!Directory.Exists(Path.Combine(appPath, "spirv")))
        {
            Directory.CreateDirectory(Path.Combine(appPath, "spirv"));
        }

        File.WriteAllBytes(filePathVetex, vertexShader.Source);
        File.WriteAllBytes(filePathFragment, fragmentShader.Source);

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

        GPUAttachmentLayout attachmentLayout = MainRenderTarget.FrameBuffer.AttachmentLayout;

        GraphicsPipelineDescriptor pipelineDescriptor = new GraphicsPipelineDescriptor(
            new GPUBindGroup[] { bindGroup },
            new ShaderModule[] { vertexShader, fragmentShader },
            new VertexInputLayout[] { vertexLayout },
            rasterizer,
            blend,
            depthStencil,
            new PixelFormat[] { attachmentLayout.Colors[0].Format },
            attachmentLayout.Depth.HasValue ? attachmentLayout.Depth.Value.Format : null,
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