using Alco.Graphics;
using Alco.ShaderCompiler;

namespace Alco.Rendering;

// ─────────────────────────────────────────────────────────────────────────────
// RenderingSystem × ShaderSystem (plan §4.2): the engine-owned module shader
// factory. The host (GameEngine) installs the module resolver — backed by the
// asset system — before the first GetShader; ".slang" assets load through
// AssetLoaderShaderSlang, which lands here.
// ─────────────────────────────────────────────────────────────────────────────

public partial class RenderingSystem
{
    private ShaderSystem? _shaderSystem;
    private SlangFileResolver? _moduleResolver;
    private readonly Lock _shaderSystemLock = new();

    /// <summary>
    /// Installs the module source resolver (module-name probes → source text).
    /// Must be set before the first <see cref="ShaderSystem"/> use; the host
    /// does this during startup.
    /// </summary>
    public void SetShaderModuleResolver(SlangFileResolver resolver)
    {
        lock (_shaderSystemLock)
        {
            _moduleResolver = resolver;
            // A resolver change invalidates everything a previous system knew.
            _shaderSystem?.Dispose();
            _shaderSystem = null;
        }
    }

    /// <summary>The module-backed shader factory (module cache, disk caches, hot reload).</summary>
    public ShaderSystem ShaderSystem
    {
        get
        {
            lock (_shaderSystemLock)
            {
                if (_shaderSystem == null)
                {
                    if (_moduleResolver == null)
                    {
                        throw new InvalidOperationException(
                            "The slang module resolver is not installed; the host must call " +
                            "SetShaderModuleResolver before any module shader is requested.");
                    }
                    _shaderSystem = new ShaderSystem(this, new SlangCompilerOptions
                    {
                        Resolver = _moduleResolver,
                        Target = SlangCodeTargetFor(GraphicsDevice.Backend),
                    }, ShaderModuleCacheDirectory);
                }
                return _shaderSystem;
            }
        }
    }

    /// <summary>
    /// The slang code format wgpu's shader passthrough consumes per backend:
    /// Vulkan/SPIR-V, D3D12/DXIL, Metal/MSL. Unknown backends keep SPIR-V, which
    /// still has the Naga-import fallback when passthrough is unavailable.
    /// </summary>
    internal static SlangCodeTarget SlangCodeTargetFor(GraphicsBackend backend) => backend switch
    {
        GraphicsBackend.D3D12 => SlangCodeTarget.Dxil,
        GraphicsBackend.Metal => SlangCodeTarget.Msl,
        _ => SlangCodeTarget.Spirv,
    };

    /// <summary>
    /// Disk-cache root for slang modules/programs; null disables caching.
    /// Defaults to the engine-provided cache directory (GraphicsSetting's
    /// shader cache path), or the built-in location when unset.
    /// </summary>
    protected internal virtual string? ShaderModuleCacheDirectory
        => SlangCacheDirectory ?? ".cache/shader-slang";

    private void OnDisposeShaderSystem()
    {
        _shaderSystem?.Dispose();
        _shaderSystem = null;
    }
}
