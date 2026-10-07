using System.Runtime.CompilerServices;
using Alco.Graphics;

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
        // Every backend consumes the same slang->SPIR-V bytes: Vulkan through
        // native SPIR-V passthrough, DX12 and Metal through wgpu's standard
        // Naga path (naga owns the per-backend resource layout translation, so
        // direct DXIL/MSL/metallib passthrough is not used by the engine).
        // The passthrough choice is declared per module in BuildModulesInfo —
        // alco-gpu validates and executes the caller's decision, it never
        // infers one. The compiler additionally normalizes Slang's default-only
        // switch wrappers that Naga's SPIR-V frontend miscompiles on every
        // Naga-consuming backend (the producer-side workaround keeps alco-gpu
        // agnostic of the shader origin).
        SlangCodeTarget target = SlangCodeTarget.Spirv;
        bool nagaConsumesSpirv = GraphicsDevice.Backend
            is GraphicsBackend.WGPUDx12 or GraphicsBackend.WGPUMetal;
        return new ShaderSystem(this, new SlangCompilerOptions
        {
            Resolver = moduleResolver,
            Target = target,
            NormalizeSpirvForNaga = nagaConsumesSpirv,
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
}
