using System.Collections.Frozen;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Loads embedded 3D instanced scene Slang modules into a <see cref="ShaderSystem"/>
/// and composes custom <c>IMeshScene3DSurface</c> shaders.
/// </summary>
public static class AlcoMesh3DShaders
{
    /// <summary>Surface interface and struct module imported by <c>IMeshScene3DSurface</c> and custom 3D scene shaders.</summary>
    public const string MeshScene3DContractModule = "AlcoRendering_MeshScene3D";

    /// <summary>Compute shader module that resets per-bucket indexed indirect draw arguments.</summary>
    public const string MeshScene3DResetArgsModule = "MeshScene3DResetArgs";

    /// <summary>Compute shader module that performs 3D frustum culling and compacts visible slots per bucket.</summary>
    public const string MeshScene3DCullModule = "MeshScene3DCull";

    /// <summary>Pass template module name used by <see cref="ComposeMeshScene3DSurface"/>.</summary>
    public const string MeshScene3DPassTemplateModule = "MeshScene3DPass";

    /// <summary>Built-in generic textured 3D mesh surface module.</summary>
    public const string MeshScene3DSurfaceStandardModule = "MeshScene3DSurfaceStandard";

    /// <summary>Built-in embedded module names in topological dependency order.</summary>
    public static readonly IReadOnlyList<string> BuiltInModuleOrder =
    [
        MeshScene3DContractModule,
        MeshScene3DResetArgsModule,
        MeshScene3DCullModule,
        MeshScene3DPassTemplateModule,
        MeshScene3DSurfaceStandardModule,
    ];

    private static readonly FrozenSet<string> BuiltInModuleSet =
        BuiltInModuleOrder.ToFrozenSet(StringComparer.Ordinal);

    private static readonly ConditionalWeakTable<ShaderCompiler, HashSet<string>> LoadedCompilers = new();
    private static readonly Lock LoadLock = new();

    /// <summary>Reads an embedded <c>.slang</c> resource shipped with <see cref="Alco.Rendering"/>.</summary>
    /// <param name="logicalName">Embedded resource name (for example <c>MeshScene3DPass.slang</c>).</param>
    /// <returns>The UTF-8 shader source text.</returns>
    public static string LoadEmbeddedSource(string logicalName)
    {
        ArgumentException.ThrowIfNullOrEmpty(logicalName);
        Assembly assembly = typeof(AlcoMesh3DShaders).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(logicalName)
            ?? throw new FileNotFoundException(
                $"Embedded 3D shader '{logicalName}' was not found in {assembly.FullName}.");
        using StreamReader reader = new(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>Attempts to resolve a built-in embedded 3D scene shader source by path or module name.</summary>
    /// <param name="pathOrModuleName">A virtual path, file name, or module identifier.</param>
    /// <param name="source">Receives the UTF-8 shader source when found.</param>
    /// <returns><see langword="true"/> when the identifier matches a built-in module.</returns>
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
    /// and falls back to built-in embedded 3D scene and scatter shader sources.
    /// </summary>
    /// <param name="primary">Optional primary file resolver checked before built-in shaders.</param>
    /// <returns>The composed file resolver delegate.</returns>
    public static ShaderFileResolver CreateFileResolver(ShaderFileResolver? primary = null)
    {
        ShaderFileResolver spriteFallback = AlcoSprite2DShaders.CreateFileResolver(primary);
        return path =>
        {
            if (TryResolveBuiltInSource(path, out string? builtIn))
            {
                string? custom = primary?.Invoke(path);
                return custom ?? builtIn;
            }

            return spriteFallback(path);
        };
    }

    /// <summary>Ensures all built-in 3D scene and instance-scatter Slang modules are loaded into <paramref name="shaders"/>.</summary>
    /// <param name="shaders">The target shader system.</param>
    public static void EnsureBuiltInModulesLoaded(ShaderSystem shaders)
    {
        ArgumentNullException.ThrowIfNull(shaders);
        AlcoSprite2DShaders.EnsureBuiltInModulesLoaded(shaders);

        ShaderCompiler compiler = shaders.Compiler;
        lock (LoadLock)
        {
            if (!LoadedCompilers.TryGetValue(compiler, out HashSet<string>? loaded))
            {
                loaded = new HashSet<string>(StringComparer.Ordinal);
                LoadedCompilers.Add(compiler, loaded);
            }

            if (loaded.Count == BuiltInModuleOrder.Count)
            {
                return;
            }

            for (int i = 0; i < BuiltInModuleOrder.Count; i++)
            {
                string moduleName = BuiltInModuleOrder[i];
                if (loaded.Contains(moduleName))
                {
                    continue;
                }

                compiler.RegisterVirtualModule(moduleName, moduleName + ".slang",
                    LoadEmbeddedSource(moduleName + ".slang"));
                loaded.Add(moduleName);
            }
        }
    }

    /// <summary>Composes a custom <c>IMeshScene3DSurface</c> module resolvable through <paramref name="shaders"/> with <see cref="MeshScene3DPassTemplateModule"/>.</summary>
    /// <param name="shaders">The target shader system.</param>
    /// <param name="surfaceModule">The surface Slang module name exporting an <c>IMeshScene3DSurface</c> implementation.</param>
    /// <param name="name">Optional diagnostic shader name.</param>
    /// <returns>The composed interned shader; pass <c>alphaTest</c> (<see cref="bool"/>) to <see cref="Shader.CreateMaterial"/>.</returns>
    public static Shader ComposeMeshScene3DSurface(ShaderSystem shaders, string surfaceModule, string name = "")
    {
        ArgumentNullException.ThrowIfNull(shaders);
        ArgumentException.ThrowIfNullOrEmpty(surfaceModule);
        EnsureBuiltInModulesLoaded(shaders);
        return shaders.Compose(MeshScene3DPassTemplateModule, surfaceModule, ShaderKind.Graphics, name);
    }

    /// <summary>Loads an in-memory <c>IMeshScene3DSurface</c> module into <paramref name="shaders"/> and composes it with <see cref="MeshScene3DPassTemplateModule"/>.</summary>
    /// <param name="shaders">The target shader system.</param>
    /// <param name="surfaceModule">Logical module name matching the <c>module</c> declaration in <paramref name="source"/>.</param>
    /// <param name="virtualPath">Virtual path for diagnostics and relative includes.</param>
    /// <param name="source">Slang source text importing <see cref="MeshScene3DContractModule"/>.</param>
    /// <param name="name">Optional diagnostic shader name.</param>
    /// <returns>The composed interned shader; pass <c>alphaTest</c> (<see cref="bool"/>) to <see cref="Shader.CreateMaterial"/>.</returns>
    public static Shader ComposeMeshScene3DSurfaceFromSource(
        ShaderSystem shaders,
        string surfaceModule,
        string virtualPath,
        string source,
        string name = "")
    {
        ArgumentNullException.ThrowIfNull(shaders);
        ArgumentException.ThrowIfNullOrEmpty(surfaceModule);
        ArgumentException.ThrowIfNullOrEmpty(virtualPath);
        ArgumentNullException.ThrowIfNull(source);

        EnsureBuiltInModulesLoaded(shaders);
        shaders.Compiler.RegisterVirtualModule(surfaceModule, virtualPath, source);
        return shaders.Compose(MeshScene3DPassTemplateModule, surfaceModule, ShaderKind.Graphics, name);
    }
}
