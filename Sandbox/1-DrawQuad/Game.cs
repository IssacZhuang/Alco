using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Alco.Graphics;
using Alco.Engine;
using Alco.ShaderCompiler;

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
    private GPUPipeline _pipeline;
    public Game(GameEngineSetting setting) : base(setting)
    {
        _commandBuffer = GraphicsDevice.CreateCommandBuffer();
        _vertexBuffer = CreateVertexBuffer();
        _indexBuffer = CreateIndexBuffer();
        _pipeline = CreatePipeline();
    }

    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }

        if (MainPresenter.FrameBuffer is not { } frameBuffer)
        {
            return;
        }

        _commandBuffer.Begin();
        using (var renderPass = _commandBuffer.BeginRender(frameBuffer))
        {
            renderPass.SetPipeline(_pipeline);
            renderPass.SetVertexBuffer(0, _vertexBuffer);
            renderPass.SetIndexBuffer(_indexBuffer, IndexFormat.UInt16);
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

    private GPUPipeline CreatePipeline()
    {
        // slang module program: every [shader(...)] entry point compiled to SPIR-V
        SlangProgram program = CompileProgram("sandbox1_shader", "shader.slang");
        ShaderModule vertexShader = StageModule(program, "MainVS");
        ShaderModule fragmentShader = StageModule(program, "MainPS");

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

        GPUAttachmentLayout attachmentLayout = MainPresenter.AttachmentLayout!;

        GraphicsPipelineDescriptor pipelineDescriptor = new GraphicsPipelineDescriptor(
            Array.Empty<GPUBindGroup>(),
            new ShaderModule[] { vertexShader, fragmentShader },
            new VertexInputLayout[] { vertexLayout },
            rasterizer,
            blend,
            depthStencil,
            new PixelFormat[] { attachmentLayout.Colors[0].Format },
            attachmentLayout.Depth.HasValue ? attachmentLayout.Depth.Value.Format : null,
            name: "quad_pipeline"
        );

        return GraphicsDevice.CreateGraphicsPipeline(pipelineDescriptor);
    }

    private SlangProgram CompileProgram(string moduleName, string fileName)
    {
        string path = Path.Combine("Assets", fileName);
        SlangModuleSystem modules = RenderingSystem.ShaderSystem.Modules;
        modules.GetOrLoadModule(moduleName, path, File.ReadAllText(path));
        return modules.GetProgramAllEntries(moduleName, []);
    }

    private static ShaderModule StageModule(SlangProgram program, string entryName)
    {
        for (int i = 0; i < program.EntryPoints.Count; i++)
        {
            if (program.EntryPoints[i].Name == entryName)
            {
                return new ShaderModule(
                    SlangCompileSession.SlangStageToEngine(program.EntryPoints[i].Stage),
                    ShaderLanguage.SPIRV,
                    program.EntryCode[i],
                    "main");
            }
        }
        throw new ArgumentException($"Entry point '{entryName}' not found in module '{program.ModuleName}'.");
    }

    private static byte[] LoadFile(string path)
    {
        return File.ReadAllBytes(Path.Combine("Assets", path));
    }
}