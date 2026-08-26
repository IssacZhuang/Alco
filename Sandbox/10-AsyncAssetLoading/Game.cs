using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Alco.Graphics;
using Alco.Engine;
using Alco.Rendering;
using Alco.ShaderCompiler;
using Alco;
using Alco.IO;

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
    private GPUPipeline _pipeline;
    private GPUBindGroup _textureGroup;
    private GPUResourceGroup _resourceGroupBuffer;
    private GPUResourceGroup? _resourceGroupTexture;
    private Texture2D? _resourceGroupTextureSource;
    private Texture2D _textureEmpty;
    private Texture2D _selected;


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

        UpdateColor(new Vector3(1, 1, 1));

        // The shader's material block declares a Texture2D member followed by a
        // SamplerState member: group 1 is the two-entry layout {texture, sampler},
        // and the sampler is supplied from the rendering system's shared bank
        // (asset textures default to linear filtering with repeat addressing).
        _textureGroup = GraphicsDevice.CreateBindGroup(new BindGroupDescriptor
        {
            Name = "texture_sampler_group",
            Bindings = new BindGroupEntry[]
            {
                new BindGroupEntry(0, ShaderStage.Standard, BindingType.Texture, new TextureBindingInfo(TextureViewDimension.Texture2D)),
                new BindGroupEntry(1, ShaderStage.Standard, BindingType.Sampler),
            },
        });

        _pipeline = CreatePipeline(GraphicsDevice.BindGroupUniformBuffer, _textureGroup);
        _resourceGroupBuffer = CreateResourceGroup(GraphicsDevice.BindGroupUniformBuffer, _colorBuffer);

        _textureEmpty = RenderingSystem.CreateTexture2D(16, 16, 0xffffffff);
        _selected = _textureEmpty;

        //DirectoryFileSource fileSource = new DirectoryFileSource("AssetSystem");
        //AssetSystem.AddFileSource(fileSource);
    }

    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }

        if (Input.IsKeyDown(KeyCode.Space))
        {
            // AssetSystem.LoadAsync<Texture2D>("test.png", (texture, exception) =>
            // {
            //     _selected = texture;
            // });

            LoadTexture();
        }

        _timer += delta;

        if (MainPresenter.FrameBuffer is not { } frameBuffer) return;
        _commandBuffer.Begin();
        using (var renderPass = _commandBuffer.BeginRender(frameBuffer))
        {
            renderPass.SetPipeline(_pipeline);
            renderPass.SetVertexBuffer(0, _vertexBuffer);
            renderPass.SetIndexBuffer(_indexBuffer, IndexFormat.UInt16);
            renderPass.SetResources(0, _resourceGroupBuffer);
            renderPass.SetResources(1, GetTextureResourceGroup(_selected));
            renderPass.DrawIndexed((uint)Indices.Length, 1, 0, 0, 0);
        }
        _commandBuffer.End();
        GraphicsDevice.Submit(_commandBuffer);
    }

    private async void LoadTexture()
    {
        Texture2D texture = await AssetSystem.LoadAsync<Texture2D>("test.png");
        _selected = texture;
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
        // slang module program: every [shader(...)] entry point compiled to SPIR-V
        SlangProgram program = CompileProgram("sandbox10_shader", "shader.slang");
        ShaderModule vertexShader = StageModule(program, "MainVS");
        ShaderModule fragmentShader = StageModule(program, "MainPS");

        Log.Info(program.Reflection);

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

        GPUAttachmentLayout attachmentLayout = MainPresenter.AttachmentLayout!;

        GraphicsPipelineDescriptor pipelineDescriptor = new GraphicsPipelineDescriptor(
            new GPUBindGroup[] { bindGroupBuffer, bindGroupTexture },
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

    // The selected texture can be swapped at runtime (async asset loading), so
    // the two-entry {texture, sampler} group is cached per texture reference.
    private GPUResourceGroup GetTextureResourceGroup(Texture2D texture)
    {
        if (_resourceGroupTexture == null || !ReferenceEquals(_resourceGroupTextureSource, texture))
        {
            _resourceGroupTexture?.Dispose();
            _resourceGroupTexture = GraphicsDevice.CreateResourceGroup(new ResourceGroupDescriptor(
                _textureGroup,
                new ResourceBindingEntry[]
                {
                    new ResourceBindingEntry(0, texture.View),
                    new ResourceBindingEntry(1, RenderingSystem.Samplers.LinearRepeat),
                },
                "texture_sampler_resources"));
            _resourceGroupTextureSource = texture;
        }

        return _resourceGroupTexture;
    }

    private void UpdateColor(Vector3 color)
    {
        GraphicsDevice.WriteBuffer(_colorBuffer, 0, color);
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