// Example-level shader system owning a long-lived Slang compiler and interned multi-variant shaders.


namespace Alco.Graphics;

/// <summary>Owns a long-lived <see cref="ShaderCompiler"/> and caches interned <see cref="Shader"/> handles for lazy specialization and composition.</summary>
public sealed class ShaderSystem : IDisposable
{
    private readonly RenderingSystem _system;
    private readonly ShaderCompiler _compiler;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, Shader> _shaders = new(StringComparer.Ordinal);
    private bool _disposed;

    /// <summary>Creates a shader system targeting <paramref name="system"/>'s device with optional compiler configuration.</summary>
    /// <param name="system">The rendering system owning the GPU device.</param>
    /// <param name="options">Optional compiler search paths, defines, virtual file resolver, and cache settings.</param>
    public ShaderSystem(RenderingSystem system, ShaderCompilerOptions? options = null)
    {
        _system = system ?? throw new ArgumentNullException(nameof(system));
        _compiler = new ShaderCompiler(system.Device, options);
    }

    /// <summary>The underlying Slang shader compiler owned by this system.</summary>
    public ShaderCompiler Compiler
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _compiler;
        }
    }

    /// <summary>Gets or creates the interned multi-variant shader for <paramref name="moduleName"/>, inferring its kind when <paramref name="kind"/> is null.</summary>
    /// <param name="moduleName">The module name in the compiler's search paths.</param>
    /// <param name="kind">Optional explicit pipeline kind; null infers Graphics, Compute, or Mesh automatically.</param>
    /// <param name="name">Optional diagnostic name; defaults to <paramref name="moduleName"/>.</param>
    /// <returns>The cached <see cref="Shader"/> handle owned by this system.</returns>
    public Shader GetShader(string moduleName, ShaderKind? kind = null, string name = "")
    {
        ArgumentException.ThrowIfNullOrEmpty(moduleName);
        string key = BuildKey(moduleName, surfaceModule: null, kind);
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_shaders.TryGetValue(key, out Shader? existing))
                return existing;
            Shader created = new(_system, _compiler, moduleName, kind, name);
            _shaders.Add(key, created);
            return created;
        }
    }

    /// <summary>Gets or creates the interned multi-variant shader from in-memory Slang source.</summary>
    /// <param name="moduleName">The unique logical module name.</param>
    /// <param name="virtualPath">The virtual path used for diagnostics and relative includes.</param>
    /// <param name="source">The Slang source text.</param>
    /// <param name="kind">Optional explicit pipeline kind; null infers Graphics, Compute, or Mesh automatically.</param>
    /// <param name="name">Optional diagnostic name; defaults to <paramref name="moduleName"/>.</param>
    /// <returns>The cached <see cref="Shader"/> handle owned by this system.</returns>
    public Shader GetShaderFromSource(string moduleName, string virtualPath, string source, ShaderKind? kind = null, string name = "")
    {
        ArgumentException.ThrowIfNullOrEmpty(moduleName);
        string key = BuildKey(moduleName, surfaceModule: null, kind);
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_shaders.TryGetValue(key, out Shader? existing))
                return existing;
            ShaderModule parsed = _compiler.LoadModuleFromSource(moduleName, virtualPath, source);
            Shader created = new(_system, parsed, kind, name);
            _shaders.Add(key, created);
            return created;
        }
    }

    /// <summary>Gets or creates the interned composed shader pairing <paramref name="templateModule"/> with <paramref name="surfaceModule"/>.</summary>
    /// <param name="templateModule">The pass template module declaring generic entry points.</param>
    /// <param name="surfaceModule">The companion module exporting a single struct implementing the template's contract.</param>
    /// <param name="kind">Optional explicit pipeline kind; null infers automatically from the template.</param>
    /// <param name="name">Optional diagnostic name; defaults to <c>template+surface</c>.</param>
    /// <returns>The cached composed <see cref="Shader"/> handle owned by this system.</returns>
    public Shader Compose(string templateModule, string surfaceModule, ShaderKind? kind = null, string name = "")
    {
        ArgumentException.ThrowIfNullOrEmpty(templateModule);
        ArgumentException.ThrowIfNullOrEmpty(surfaceModule);
        string key = BuildKey(templateModule, surfaceModule, kind);
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_shaders.TryGetValue(key, out Shader? existing))
                return existing;
            Shader created = Shader.Compose(_system, _compiler, templateModule, surfaceModule, kind, name);
            _shaders.Add(key, created);
            return created;
        }
    }

    /// <summary>Destroys all interned shaders and disposes the owned compiler.</summary>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            _disposed = true;
            foreach (Shader shader in _shaders.Values)
            {
                if (!shader.IsDestroyed)
                    shader.Destroy();
            }
            _shaders.Clear();
        }
        _compiler.Dispose();
    }

    private static string BuildKey(string module, string? surfaceModule, ShaderKind? kind)
    {
        string kindTag = kind.HasValue ? ((int)kind.Value).ToString() : "auto";
        return surfaceModule == null ? $"{module}|{kindTag}" : $"{module}+{surfaceModule}|{kindTag}";
    }
}
