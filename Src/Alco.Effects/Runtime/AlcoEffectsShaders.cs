using System.Collections.Frozen;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using Alco.Graphics;

namespace Alco.Effects;

/// <summary>
/// Provides access to built-in <c>Alco.Effects</c> embedded Slang shader modules and
/// ensures they are registered into a <see cref="ShaderSystem"/> in dependency order.
/// </summary>
public static class AlcoEffectsShaders
{
    private static readonly ConditionalWeakTable<ShaderCompiler, HashSet<string>> LoadedCompilers = new();
    private static readonly Lock LoadLock = new();

    /// <summary>Built-in embedded module names in topological dependency order.</summary>
    public static readonly IReadOnlyList<string> BuiltInModuleOrder =
    [
        "AlcoParticles_Core2D",
        "AlcoParticles_Core3D",
        "AlcoParticles_Default2D",
        "AlcoParticles_Default3D",
        "AlcoParticles_Surface",
        "ParticleSurfaceDefault",
        "GpuParticleResetArgs",
        "GpuParticleInit2D",
        "GpuParticleInit3D",
        "GpuParticleEmit2D",
        "GpuParticleEmit3D",
        "GpuParticleSimulate2D",
        "GpuParticleSimulate3D",
        "GpuParticle2D",
        "GpuParticle3D",
        "AlcoRendering_TrailNoise",
        "AlcoRendering_Trails2D",
        "AlcoRendering_Trails3D",
        "AlcoRendering_TrailSurface",
        "TrailSurfaceDefault",
        "TrailSurfaceSmoke",
        "GpuTrail2D",
        "GpuTrail3D",
    ];

    private static readonly FrozenSet<string> BuiltInModuleSet =
        BuiltInModuleOrder.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Reads an embedded <c>.slang</c> resource shipped with <see cref="Alco.Effects"/>.</summary>
    /// <param name="logicalName">Embedded resource name (for example <c>GpuParticle2D.slang</c>).</param>
    /// <returns>The UTF-8 shader source text.</returns>
    public static string LoadEmbeddedSource(string logicalName)
    {
        ArgumentException.ThrowIfNullOrEmpty(logicalName);
        Assembly assembly = typeof(AlcoEffectsShaders).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(logicalName)
            ?? throw new FileNotFoundException(
                $"Embedded effects shader '{logicalName}' was not found in {assembly.FullName}.");
        using StreamReader reader = new(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>Resolves a built-in embedded shader module by path or module name.</summary>
    /// <param name="pathOrModuleName">A virtual path, file name, or module identifier.</param>
    /// <param name="source">Receives the UTF-8 shader source when found.</param>
    /// <returns>True when the identifier matches a built-in module.</returns>
    public static bool TryResolveBuiltInSource(string pathOrModuleName, out string? source)
    {
        if (!string.IsNullOrWhiteSpace(pathOrModuleName))
        {
            string fileName = Path.GetFileNameWithoutExtension(pathOrModuleName.Trim()).Replace('-', '_');
            if (BuiltInModuleSet.Contains(fileName))
            {
                source = LoadEmbeddedSource(fileName + ".slang");
                return true;
            }
        }
        source = null;
        return false;
    }

    /// <summary>
    /// Creates a <see cref="ShaderFileResolver"/> that consults <paramref name="primary"/> first
    /// and falls back to built-in embedded <c>Alco.Effects</c> shader sources.
    /// </summary>
    /// <param name="primary">Optional primary virtual file resolver checked before built-in shaders.</param>
    /// <returns>The composed file resolver delegate.</returns>
    public static ShaderFileResolver CreateFileResolver(ShaderFileResolver? primary = null)
        => path =>
        {
            string? resolved = primary?.Invoke(path);
            if (resolved != null)
                return resolved;
            return TryResolveBuiltInSource(path, out string? builtIn) ? builtIn : null;
        };

    /// <summary>Ensures all built-in particle and trail Slang modules are loaded into <paramref name="shaders"/>.</summary>
    /// <param name="shaders">The target shader system.</param>
    public static void EnsureBuiltInModulesLoaded(ShaderSystem shaders)
    {
        ArgumentNullException.ThrowIfNull(shaders);
        ShaderCompiler compiler = shaders.Compiler;
        lock (LoadLock)
        {
            if (!LoadedCompilers.TryGetValue(compiler, out HashSet<string>? loaded))
            {
                loaded = new HashSet<string>(StringComparer.Ordinal);
                LoadedCompilers.Add(compiler, loaded);
            }

            if (loaded.Count == BuiltInModuleOrder.Count)
                return;

            for (int i = 0; i < BuiltInModuleOrder.Count; i++)
            {
                string moduleName = BuiltInModuleOrder[i];
                if (loaded.Contains(moduleName))
                    continue;
                compiler.LoadModuleFromSource(moduleName, moduleName + ".slang",
                    LoadEmbeddedSource(moduleName + ".slang"));
                loaded.Add(moduleName);
            }
        }
    }
}
