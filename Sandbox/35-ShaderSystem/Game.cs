using System.Numerics;
using System.Runtime.InteropServices;
using Alco;
using Alco.Engine;
using Alco.Graphics;
using Alco.IO;
using Alco.Rendering;
using Alco.ShaderCompiler;

public class Game : GameEngine
{
    #region Geometry

    [StructLayout(LayoutKind.Sequential)]
    private struct Vertex
    {
        public Vector3 Position;
        public Vector2 TexCoord;
    }

    private static readonly Vertex[] Vertices =
    {
        new() { Position = new Vector3(-0.5f, 0.5f, 0.5f), TexCoord = new Vector2(0.0f, 0.0f) },
        new() { Position = new Vector3(0.5f, 0.5f, 0.5f), TexCoord = new Vector2(1.0f, 0.0f) },
        new() { Position = new Vector3(0.5f, -0.5f, 0.5f), TexCoord = new Vector2(1.0f, 1.0f) },
        new() { Position = new Vector3(-0.5f, -0.5f, 0.5f), TexCoord = new Vector2(0.0f, 1.0f) },
    };

    private static readonly ushort[] Indices = { 0, 1, 2, 0, 2, 3 };

    #endregion

    private const string QuadModule = "alco-sandbox-shadersystem-quad";
    // AssetSystem paths are relative to the Assets/ root.
    private const string CoreAssetPath = "Shaders/Libs/alco-rendering-core.slang";
    private const string QuadAssetPath = "Shaders/alco-sandbox-shadersystem-quad.slang";

    private GPUCommandBuffer _commandBuffer = null!;
    private GPUBuffer _vertexBuffer = null!;
    private GPUBuffer _indexBuffer = null!;
    private GPUBuffer _frameBuffer = null!;
    private GPUResourceGroup _frameResources = null!;
    private Texture2D _texture = null!;

    private ShaderSystem _shaderSystem = null!;
    private Shader _shader = null!;
    private GraphicsPipelineContext _pipelineInfo;

    private FileSystemWatcher? _watcher;
    private volatile string? _pendingChange;
    private float _timer;

    public Game(GameEngineSetting setting) : base(setting)
    {
        _commandBuffer = GraphicsDevice.CreateCommandBuffer();
        _vertexBuffer = CreateVertexBuffer();
        _indexBuffer = CreateIndexBuffer();

        _frameBuffer = GraphicsDevice.CreateBuffer(new BufferDescriptor
        {
            Name = "Frame Constants",
            Size = (uint)(Marshal.SizeOf<Matrix4x4>() + Marshal.SizeOf<Vector4>()),
            Usage = BufferUsage.Uniform | BufferUsage.CopyDst,
        });
        _frameResources = GraphicsDevice.CreateResourceGroup(new ResourceGroupDescriptor
        {
            Layout = GraphicsDevice.BindGroupUniformBuffer,
            Resources = new ResourceBindingEntry[]
            {
                new(0, _frameBuffer),
            },
        });

        _texture = RenderingSystem.CreateTexture2D(16, 16, 0xff5b8cff);

        // The module-name keyed factory (plan §4.2): no asset load, no text mode.
        // The virtual file system resolves module names against the game's own
        // Assets/Shaders tree plus the engine's ported core module.
        _shaderSystem = new ShaderSystem(RenderingSystem, new SlangCompilerOptions
        {
            Resolver = ResolveShaderModule,
        });
        _shader = _shaderSystem.GetShader(QuadModule);
        _pipelineInfo = _shader.GetGraphicsPipeline(
            RenderingSystem.PreferredHDRPass,
            DepthStencilState.Default,
            BlendState.NonPremultipliedAlpha);

        WatchShaderSources();
    }

    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }

        // Hot reload: a changed module was invalidated during load; map the
        // watcher path into the dependency graph and drop the affected modules.
        if (_pendingChange is { } changed)
        {
            _pendingChange = null;
            InvalidateByFileName(changed);
        }

        _timer += delta;
        if (MainPresenter.FrameBuffer is not { } frameBuffer)
        {
            return;
        }

        // Rebuilds lazily when the shader version changed (module invalidation).
        _shader.TryUpdatePipelineContext(ref _pipelineInfo, frameBuffer.AttachmentLayout);

        Matrix4x4 rotation = Matrix4x4.CreateRotationY(_timer) * Matrix4x4.CreateRotationZ(_timer * 0.5f);
        GraphicsDevice.WriteBuffer(_frameBuffer, 0, rotation);
        GraphicsDevice.WriteBuffer(_frameBuffer, (uint)Marshal.SizeOf<Matrix4x4>(),
            new Vector4(1.0f, 0.9f, 0.8f, 1.0f));

        _commandBuffer.Begin();
        using var renderPass = _commandBuffer.BeginRender(frameBuffer);
        {
            renderPass.SetPipeline(_pipelineInfo);
            renderPass.SetVertexBuffer(0, _vertexBuffer);
            renderPass.SetIndexBuffer(_indexBuffer, IndexFormat.UInt16);
            renderPass.SetResources(0, _frameResources);
            renderPass.SetResources(1, _texture.EntrySample);
            renderPass.DrawIndexed((uint)Indices.Length, 1, 0, 0, 0);
        }
        _commandBuffer.End();
        GraphicsDevice.Submit(_commandBuffer);
    }

    protected override void OnStop()
    {
        _watcher?.Dispose();
        _shaderSystem.Dispose();
    }

    /// <summary>
    /// Serves slang module requests: exact asset paths first (relative to the
    /// Assets/ root), then the module-name → file forms slang probes
    /// ('a/b.slang', 'a-b.slang') matched by dashed file name — the same
    /// convention the engine integration uses.
    /// </summary>
    private string? ResolveShaderModule(string path)
    {
        string key = SlangPathUtility.NormalizePath(path);
        if (key.StartsWith("assets/", StringComparison.OrdinalIgnoreCase))
        {
            key = key["assets/".Length..];
        }
        if (AssetSystem.TryGetStream(key, out Stream? exact))
        {
            using (exact)
            {
                using StreamReader reader = new(exact, System.Text.Encoding.UTF8);
                return reader.ReadToEnd();
            }
        }

        string dashed = key.Replace('/', '-');
        if (dashed.EndsWith("alco-rendering-core.slang", StringComparison.OrdinalIgnoreCase))
        {
            return ReadAssetText(CoreAssetPath);
        }
        if (dashed.EndsWith("alco-sandbox-shadersystem-quad.slang", StringComparison.OrdinalIgnoreCase))
        {
            return ReadAssetText(QuadAssetPath);
        }
        return null;
    }

    private string ReadAssetText(string assetPath)
    {
        if (!AssetSystem.TryGetStream(assetPath, out Stream? stream))
        {
            throw new FileNotFoundException($"Shader module asset '{assetPath}' was not found.");
        }
        using (stream)
        {
            using StreamReader reader = new(stream, System.Text.Encoding.UTF8);
            return reader.ReadToEnd();
        }
    }

    private void WatchShaderSources()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Assets", "Shaders");
        if (!Directory.Exists(directory))
        {
            return;
        }
        _watcher = new FileSystemWatcher(directory)
        {
            IncludeSubdirectories = true,
            EnableRaisingEvents = true,
        };
        _watcher.Changed += (_, args) => _pendingChange = args.FullPath;
        _watcher.Created += (_, args) => _pendingChange = args.FullPath;
    }

    /// <summary>Maps a changed file (by dashed name) onto the recorded dependency paths.</summary>
    private void InvalidateByFileName(string fullPath)
    {
        string dashedName = Path.GetFileName(fullPath).Replace('_', '-');
        foreach (string module in _shaderSystem.Modules.GetLoadedModuleNames())
        {
            foreach (string dependency in _shaderSystem.Modules.GetModuleDependencies(module))
            {
                if (dependency.Replace('/', '-').EndsWith(dashedName, StringComparison.OrdinalIgnoreCase))
                {
                    Log.Info($"[hot reload] '{Path.GetFileName(fullPath)}' changed → invalidating module '{module}'");
                    IReadOnlyList<string> affected = _shaderSystem.InvalidateModulesContaining(dependency);
                    Log.Info($"[hot reload] affected modules: {string.Join(", ", affected)} (pipeline rebuilds lazily next frame)");
                    return;
                }
            }
        }
    }

    private GPUBuffer CreateIndexBuffer()
    {
        return GraphicsDevice.CreateBuffer(new BufferDescriptor
        {
            Name = "Quad Index Buffer",
            Size = (uint)Marshal.SizeOf<ushort>() * (uint)Indices.Length,
            Usage = BufferUsage.Index | BufferUsage.CopyDst,
        }, Indices);
    }

    private GPUBuffer CreateVertexBuffer()
    {
        return GraphicsDevice.CreateBuffer(new BufferDescriptor
        {
            Name = "Quad Vertex Buffer",
            Size = (uint)Marshal.SizeOf<Vertex>() * (uint)Vertices.Length,
            Usage = BufferUsage.Vertex | BufferUsage.CopyDst,
        }, Vertices);
    }
}
