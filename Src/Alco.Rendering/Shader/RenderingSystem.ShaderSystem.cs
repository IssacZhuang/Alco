using System.Runtime.CompilerServices;
using Alco.Graphics;
using Alco.ShaderCompiler;

namespace Alco.Rendering;

// ─────────────────────────────────────────────────────────────────────────────
// RenderingSystem × ShaderSystem (plan §4.2): the engine-owned module shader
// factory. Constructed eagerly with the rendering system (RAII, like every
// other subsystem): the module resolver — backed by the asset system as a
// plain file provider — is a constructor dependency the host supplies; a null
// resolver falls back to slang's OS file system. Callers load shaders by
// module name through here, never through asset loads.
// ─────────────────────────────────────────────────────────────────────────────

public partial class RenderingSystem
{
    private readonly ShaderSystem _shaderSystem;

    /// <summary>
    /// Creates the module-backed shader factory (module cache, disk caches, hot
    /// reload) over the given module source resolver.
    /// </summary>
    /// <param name="moduleResolver">
    /// Serves module-name probes and import paths (module-name → source text);
    /// null uses slang's OS file system (tests and disk-backed sandboxes).
    /// </param>
    /// <param name="slangCacheDirectory">Disk-cache root for slang modules/programs; null disables caching.</param>
    private ShaderSystem CreateShaderSystem(SlangFileResolver? moduleResolver, string? slangCacheDirectory)
    {
        return new ShaderSystem(this, new SlangCompilerOptions
        {
            Resolver = moduleResolver,
            Target = SlangCodeTargetFor(GraphicsDevice.Backend),
            // Cache/compile events (hit/miss with timings) — the old
            // DXC ShaderCache logged these; the slang path stayed silent.
            Log = message => Log.Info(message),
        }, slangCacheDirectory);
    }

    /// <summary>The module-backed shader factory (module cache, disk caches, hot reload).</summary>
    public ShaderSystem ShaderSystem
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _shaderSystem;
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
}
