using System.Runtime.CompilerServices;
using Alco.Graphics;
using Alco.IO;

namespace Alco.Rendering;

// ─────────────────────────────────────────────────────────────────────────────
// RenderingSystem × ShaderSystem: the engine-owned module shader
// factory. Constructed eagerly with the rendering system (RAII, like every
// other subsystem): a host-supplied module resolver stays authoritative. Without
// one, disk files resolve first and executable-adjacent Assets supply built-ins.
// Callers load shaders by module name through here, never through asset loads.
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
    /// null resolves disk files first, then modules in executable-adjacent Assets.
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
            is GraphicsBackend.Dx12 or GraphicsBackend.Metal;
        return new ShaderSystem(this, new SlangCompilerOptions
        {
            Resolver = moduleResolver ?? CreateDefaultShaderModuleResolver(),
            Target = target,
            NormalizeSpirvForNaga = nagaConsumesSpirv,
            // Forwards slang cache/compile hit-miss events with timings.
            Log = message => Log.Info(message),
        }, slangCacheDirectory);
    }

    private static SlangFileResolver CreateDefaultShaderModuleResolver()
    {
        DirectoryFileSource disk = new(".");
        DirectoryFileSource deployedAssets = new(Path.Combine(AppContext.BaseDirectory, "Assets"));
        SlangFileResolver builtIns = ShaderModuleResolver.Create(
            path => deployedAssets.TryGetStream(path, out Stream? stream, out _) ? stream : null,
            () => deployedAssets.AllFileNames);

        return path =>
        {
            // Preserve disk-backed sandboxes, including relative imports and includes.
            // Explicit host resolvers never enter this fallback path.
            if (!disk.TryGetStream(path, out Stream? stream, out _))
            {
                return builtIns(path);
            }
            using (stream)
            using (StreamReader reader = new(stream))
            {
                return reader.ReadToEnd();
            }
        };
    }

    /// <summary>The module-backed shader factory (module cache, disk caches, hot reload).</summary>
    public ShaderSystem ShaderSystem
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _shaderSystem;
    }
}
