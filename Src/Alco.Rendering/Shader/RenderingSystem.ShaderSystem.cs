using System.Runtime.CompilerServices;
using Alco.Graphics;
using Alco.ShaderCompiler;

namespace Alco.Rendering;

// ─────────────────────────────────────────────────────────────────────────────
// RenderingSystem × ShaderSystem: the engine-owned module shader
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
        bool metalLib = MetalLibTargetEnabled(GraphicsDevice.Backend, GraphicsDevice.MetalLibPassthroughSupported);
        SlangCodeTarget target = GraphicsDevice.Backend switch
        {
            GraphicsBackend.D3D12 => SlangCodeTarget.Dxil,
            GraphicsBackend.Metal => metalLib ? SlangCodeTarget.MetalLib : SlangCodeTarget.Msl,
            _ => SlangCodeTarget.Spirv,
        };
        if (GraphicsDevice.Backend == GraphicsBackend.Metal)
        {
            Log.Info(metalLib
                ? "Metal shaders compile to precompiled metallib (Apple toolchain + wgpu metallib passthrough present)"
                : "Metal shaders compile to MSL source (metallib toolchain or wgpu metallib passthrough unavailable)");
        }
        return new ShaderSystem(this, new SlangCompilerOptions
        {
            Resolver = moduleResolver,
            Target = target,
            // Forwards slang cache/compile hit-miss events with timings.
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
    /// Whether the Metal backend compiles shaders to precompiled metallib containers:
    /// both sides must agree — slang's metallib codegen (Apple's external Metal
    /// toolchain present) and wgpu-native's metallib passthrough entry (the third
    /// Alco patch). Probed once per rendering system; falls back to MSL source.
    /// </summary>
    internal static bool MetalLibTargetEnabled(GraphicsBackend backend, bool metalLibPassthrough)
    {
        if (backend != GraphicsBackend.Metal || !metalLibPassthrough)
        {
            return false;
        }
        try
        {
            return new SlangCompiler().MetalLibSupported;
        }
        catch
        {
            return false;
        }
    }
}
