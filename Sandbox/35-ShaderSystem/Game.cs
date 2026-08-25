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
    [StructLayout(LayoutKind.Sequential)]
    private struct FrameConstants
    {
        public Matrix4x4 ViewProjection;
        public Vector4 TintColor;
    }

    private const string QuadModule = "alco-sandbox-shadersystem-quad";
    // AssetSystem paths are relative to the Assets/ root.
    private const string CoreAssetPath = "Shaders/Libs/alco-rendering-core.slang";
    private const string QuadAssetPath = "Shaders/alco-sandbox-shadersystem-quad.slang";

    private GraphicsValueBuffer<FrameConstants> _frameData = null!;
    private Texture2D _texture = null!;
    private RenderContext _renderContext = null!;
    private GraphicsMaterial _material = null!;

    private ShaderSystem _shaderSystem = null!;
    private Shader _shader = null!;

    private FileSystemWatcher? _watcher;
    private volatile string? _pendingChange;
    private float _timer;

    public Game(GameEngineSetting setting) : base(setting)
    {
        _renderContext = RenderingSystem.CreateRenderContext("sandbox_shader_system");

        _frameData = RenderingSystem.CreateGraphicsValueBuffer<FrameConstants>(
            new FrameConstants
            {
                ViewProjection = Matrix4x4.Identity,
                TintColor = new Vector4(1.0f, 0.9f, 0.8f, 1.0f),
            },
            "frame_constants");

        _texture = RenderingSystem.CreateTexture2D(16, 16, 0xff5b8cff);

        // The module-name keyed factory (plan §4.2): no asset load, no text mode.
        // The virtual file system resolves module names against the game's own
        // Assets/Shaders tree plus the engine's ported core module.
        _shaderSystem = new ShaderSystem(RenderingSystem, new SlangCompilerOptions
        {
            Resolver = ResolveShaderModule,
        });
        _shader = _shaderSystem.GetShader(QuadModule);

        // Standard material consumption: the parameter set binds the shader's
        // blocks by name (the _frame uniform block, the _albedo texture slot)
        // and resolves the shared sampler bank automatically.
        _material = RenderingSystem.CreateGraphicsMaterial(_shader, "sandbox_shader_system_material");
        _material.SetBuffer("_frame", _frameData);
        _material.SetTexture("_albedo", _texture);

        WatchShaderSources();
    }

    /// <summary>
    /// Serves engine modules (the built-in shaders resolved through the AssetSystem)
    /// on top of the sandbox's own tree — the same sources every sandbox exposing
    /// engine shaders registers.
    /// </summary>
    public override IEnumerable<IFileSource> CreateDefaultFileSources()
    {
        foreach (var fileSource in base.CreateDefaultFileSources())
        {
            yield return fileSource;
        }
        yield return new DirectoryWatcherFileSource(GetSolutionAssetPath("Alco.Engine"), AssetSystem);
        yield return new DirectoryWatcherFileSource(GetSolutionAssetPath("Alco.Rendering"), AssetSystem);
    }

    private static string GetSolutionAssetPath(string project)
    {
        string? current = AppContext.BaseDirectory;
        while (current != null && Directory.GetFiles(current, "*.slnx").Length == 0)
        {
            current = Path.GetDirectoryName(current);
        }
        return Path.Combine(current ?? ".", "Src", project, "Assets");
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

        _frameData.Value = new FrameConstants
        {
            ViewProjection = Matrix4x4.CreateRotationY(_timer) * Matrix4x4.CreateRotationZ(_timer * 0.5f),
            TintColor = new Vector4(1.0f, 0.9f, 0.8f, 1.0f),
        };
        _frameData.UpdateBuffer();

        // The pipeline rebuilds lazily when the shader version changed (module invalidation).
        using (RenderFrameScope frame = _renderContext.BeginFrame())
        using (RenderPassScope pass = _renderContext.BeginPass(frameBuffer))
        {
            pass.Draw(RenderingSystem.MeshFullScreen, _material);
        }
    }

    protected override void OnStop()
    {
        _watcher?.Dispose();
        _shaderSystem.Dispose();
        _renderContext.Dispose();
        _material.Dispose();
        _frameData.Dispose();
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
}
