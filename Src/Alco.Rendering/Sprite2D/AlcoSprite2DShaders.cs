using System.Collections.Frozen;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Loads embedded 2D sprite batch and GPU-driven sprite scene Slang modules into a <see cref="ShaderSystem"/>
/// and composes custom <c>ISprite2DSurface</c> and <c>ISpriteSceneSurface</c> shaders.
/// </summary>
public static class AlcoSprite2DShaders
{
    /// <summary>Pass template module name used by <see cref="ComposeSprite2DSurface"/>.</summary>
    public const string Sprite2DPassTemplateModule = "Sprite2DPass";

    /// <summary>Surface interface module imported by custom <c>ISprite2DSurface</c> modules.</summary>
    public const string Sprite2DSurfaceContractModule = "AlcoRendering_Sprite2D";

    /// <summary>Default immediate-mode sprite shader module name.</summary>
    public const string Sprite2DDefaultModule = "Sprite2DDefault";

    /// <summary>Generic GPU instance scatter compute shader module name.</summary>
    public const string GpuInstanceScatterModule = "GpuInstanceScatter";

    /// <summary>Pass template module name used by <see cref="ComposeSpriteSceneSurface"/>.</summary>
    public const string SpriteScenePassTemplateModule = "SpriteScenePass";

    /// <summary>Surface interface module imported by custom <c>ISpriteSceneSurface</c> modules.</summary>
    public const string SpriteSceneSurfaceContractModule = "AlcoRendering_SpriteScene";

    /// <summary>Compute shader module that resets per-bucket indirect draw arguments.</summary>
    public const string SpriteSceneResetArgsModule = "SpriteSceneResetArgs";

    /// <summary>Compute shader module that culls and compacts visible slots per bucket.</summary>
    public const string SpriteSceneCullModule = "SpriteSceneCull";

    /// <summary>Built-in standard textured sprite scene surface module.</summary>
    public const string SpriteSceneSurfaceStandardModule = "SpriteSceneSurfaceStandard";

    /// <summary>Built-in embedded module names in topological dependency order.</summary>
    public static readonly IReadOnlyList<string> BuiltInModuleOrder =
    [
        Sprite2DSurfaceContractModule,
        Sprite2DDefaultModule,
        Sprite2DPassTemplateModule,
        GpuInstanceScatterModule,
        SpriteSceneSurfaceContractModule,
        SpriteSceneResetArgsModule,
        SpriteSceneCullModule,
        SpriteScenePassTemplateModule,
        SpriteSceneSurfaceStandardModule,
    ];

    private static readonly FrozenSet<string> BuiltInModuleSet =
        BuiltInModuleOrder.ToFrozenSet(StringComparer.Ordinal);

    private static readonly ConditionalWeakTable<ShaderCompiler, HashSet<string>> LoadedCompilers = new();
    private static readonly Lock LoadLock = new();

    /// <summary>Reads an embedded <c>.slang</c> resource shipped with <see cref="Alco.Rendering"/>.</summary>
    /// <param name="logicalName">Embedded resource name (for example <c>Sprite2DDefault.slang</c>).</param>
    /// <returns>The UTF-8 shader source text.</returns>
    public static string LoadEmbeddedSource(string logicalName)
    {
        ArgumentException.ThrowIfNullOrEmpty(logicalName);
        Assembly assembly = typeof(AlcoSprite2DShaders).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(logicalName)
            ?? throw new FileNotFoundException(
                $"Embedded 2D shader '{logicalName}' was not found in {assembly.FullName}.");
        using StreamReader reader = new(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>Attempts to resolve a built-in embedded 2D shader source by path or module name.</summary>
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
    /// and falls back to built-in embedded 2D pipeline shader sources.
    /// </summary>
    /// <param name="primary">Optional primary file resolver checked before built-in shaders.</param>
    /// <returns>The composed file resolver delegate.</returns>
    public static ShaderFileResolver CreateFileResolver(ShaderFileResolver? primary = null)
    {
        return path =>
        {
            string? resolved = primary?.Invoke(path);
            if (resolved != null)
            {
                return resolved;
            }

            return TryResolveBuiltInSource(path, out string? builtIn) ? builtIn : null;
        };
    }

    /// <summary>Ensures all built-in 2D sprite batch and sprite scene Slang modules are loaded into <paramref name="shaders"/>.</summary>
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

                compiler.LoadModuleFromSource(moduleName, moduleName + ".slang",
                    LoadEmbeddedSource(moduleName + ".slang"));
                loaded.Add(moduleName);
            }
        }
    }

    /// <summary>Composes a custom <c>ISprite2DSurface</c> module resolvable through <paramref name="shaders"/> with <see cref="Sprite2DPassTemplateModule"/>.</summary>
    /// <param name="shaders">The target shader system.</param>
    /// <param name="surfaceModule">The surface Slang module name exporting an <c>ISprite2DSurface</c> implementation.</param>
    /// <param name="name">Optional diagnostic shader name.</param>
    /// <returns>The composed interned shader ready for <see cref="Shader.CreateMaterial"/>.</returns>
    public static Shader ComposeSprite2DSurface(ShaderSystem shaders, string surfaceModule, string name = "")
    {
        ArgumentNullException.ThrowIfNull(shaders);
        ArgumentException.ThrowIfNullOrEmpty(surfaceModule);
        EnsureBuiltInModulesLoaded(shaders);
        return shaders.Compose(Sprite2DPassTemplateModule, surfaceModule, ShaderKind.Graphics, name);
    }

    /// <summary>Loads an in-memory <c>ISprite2DSurface</c> module into <paramref name="shaders"/> and composes it with <see cref="Sprite2DPassTemplateModule"/>.</summary>
    /// <param name="shaders">The target shader system.</param>
    /// <param name="surfaceModule">Logical module name matching the <c>module</c> declaration in <paramref name="source"/>.</param>
    /// <param name="virtualPath">Virtual path for diagnostics and relative includes.</param>
    /// <param name="source">Slang source text importing <see cref="Sprite2DSurfaceContractModule"/>.</param>
    /// <param name="name">Optional diagnostic shader name.</param>
    /// <returns>The composed interned shader ready for <see cref="Shader.CreateMaterial"/>.</returns>
    public static Shader ComposeSprite2DSurfaceFromSource(
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
        shaders.Compiler.LoadModuleFromSource(surfaceModule, virtualPath, source);
        return shaders.Compose(Sprite2DPassTemplateModule, surfaceModule, ShaderKind.Graphics, name);
    }

    /// <summary>Composes a custom <c>ISpriteSceneSurface</c> module resolvable through <paramref name="shaders"/> with <see cref="SpriteScenePassTemplateModule"/>.</summary>
    /// <param name="shaders">The target shader system.</param>
    /// <param name="surfaceModule">The surface Slang module name exporting an <c>ISpriteSceneSurface</c> implementation.</param>
    /// <param name="name">Optional diagnostic shader name.</param>
    /// <returns>The composed interned shader; pass <c>alphaTest</c> (<see cref="bool"/>) to <see cref="Shader.CreateMaterial"/>.</returns>
    public static Shader ComposeSpriteSceneSurface(ShaderSystem shaders, string surfaceModule, string name = "")
    {
        ArgumentNullException.ThrowIfNull(shaders);
        ArgumentException.ThrowIfNullOrEmpty(surfaceModule);
        EnsureBuiltInModulesLoaded(shaders);
        return shaders.Compose(SpriteScenePassTemplateModule, surfaceModule, ShaderKind.Graphics, name);
    }

    /// <summary>Loads an in-memory <c>ISpriteSceneSurface</c> module into <paramref name="shaders"/> and composes it with <see cref="SpriteScenePassTemplateModule"/>.</summary>
    /// <param name="shaders">The target shader system.</param>
    /// <param name="surfaceModule">Logical module name matching the <c>module</c> declaration in <paramref name="source"/>.</param>
    /// <param name="virtualPath">Virtual path for diagnostics and relative includes.</param>
    /// <param name="source">Slang source text importing <see cref="SpriteSceneSurfaceContractModule"/>.</param>
    /// <param name="name">Optional diagnostic shader name.</param>
    /// <returns>The composed interned shader; pass <c>alphaTest</c> (<see cref="bool"/>) to <see cref="Shader.CreateMaterial"/>.</returns>
    public static Shader ComposeSpriteSceneSurfaceFromSource(
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
        shaders.Compiler.LoadModuleFromSource(surfaceModule, virtualPath, source);
        return shaders.Compose(SpriteScenePassTemplateModule, surfaceModule, ShaderKind.Graphics, name);
    }
}
