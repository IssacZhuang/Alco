using System.Reflection;
using System.Text;
using Alco.Graphics;

namespace Alco.GUI;

/// <summary>Helpers for loading embedded Alco.GUI shaders and composing custom <c>IUISurface</c> materials.</summary>
public static class AlcoGUIShaders
{
    /// <summary>Pass template module name used by <see cref="ShaderSystem.Compose"/>.</summary>
    public const string PassTemplateModule = "AlcoGUI_Pass";

    /// <summary>Surface interface contract module imported by custom <c>IUISurface</c> shaders.</summary>
    public const string SurfaceContractModule = "AlcoGUI_Surface";

    /// <summary>Reads an embedded <c>.slang</c> resource shipped with <see cref="Alco.GUI"/>.</summary>
    /// <param name="logicalName">Embedded resource name (for example <c>AlcoGUI_Default.slang</c>).</param>
    /// <returns>The UTF-8 shader source text.</returns>
    public static string LoadEmbeddedSource(string logicalName)
    {
        ArgumentException.ThrowIfNullOrEmpty(logicalName);
        Assembly assembly = typeof(AlcoGUIShaders).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(logicalName)
            ?? throw new FileNotFoundException($"Embedded UI shader '{logicalName}' was not found in {assembly.FullName}.");
        using StreamReader reader = new(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>Ensures the embedded <c>AlcoGUI_Surface</c> and <c>AlcoGUI_Pass</c> modules are loaded into <paramref name="shaders"/>.</summary>
    /// <param name="shaders">The target shader system.</param>
    public static void EnsureTemplateModulesLoaded(ShaderSystem shaders)
    {
        ArgumentNullException.ThrowIfNull(shaders);
        shaders.Compiler.RegisterVirtualModule(SurfaceContractModule, SurfaceContractModule + ".slang",
            LoadEmbeddedSource("AlcoGUI_Surface.slang"));
        shaders.Compiler.RegisterVirtualModule(PassTemplateModule, PassTemplateModule + ".slang",
            LoadEmbeddedSource("AlcoGUI_Pass.slang"));
    }

    /// <summary>Composes a custom <c>IUISurface</c> module resolvable through <paramref name="shaders"/> with <c>AlcoGUI_Pass</c>.</summary>
    /// <param name="shaders">The target shader system.</param>
    /// <param name="surfaceModule">The surface Slang module name exporting an <c>IUISurface</c> implementation.</param>
    /// <param name="name">Optional diagnostic shader name.</param>
    /// <returns>The composed interned shader ready for <see cref="Shader.CreateMaterial"/>.</returns>
    public static Shader ComposeSurface(ShaderSystem shaders, string surfaceModule, string name = "")
    {
        ArgumentNullException.ThrowIfNull(shaders);
        ArgumentException.ThrowIfNullOrEmpty(surfaceModule);
        EnsureTemplateModulesLoaded(shaders);
        return shaders.Compose(PassTemplateModule, surfaceModule, ShaderKind.Graphics, name);
    }

    /// <summary>Loads an in-memory <c>IUISurface</c> module into <paramref name="shaders"/> and composes it with <c>AlcoGUI_Pass</c>.</summary>
    /// <param name="shaders">The target shader system.</param>
    /// <param name="surfaceModule">Unique logical module name matching the <c>module</c> declaration in <paramref name="source"/>.</param>
    /// <param name="virtualPath">Virtual path for diagnostics and relative includes.</param>
    /// <param name="source">Slang source text importing <c>AlcoGUI_Surface</c> and exporting one <c>IUISurface</c> struct.</param>
    /// <param name="name">Optional diagnostic shader name.</param>
    /// <returns>The composed interned shader ready for <see cref="Shader.CreateMaterial"/>.</returns>
    public static Shader ComposeSurfaceFromSource(
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

        EnsureTemplateModulesLoaded(shaders);
        shaders.Compiler.RegisterVirtualModule(surfaceModule, virtualPath, source);
        return shaders.Compose(PassTemplateModule, surfaceModule, ShaderKind.Graphics, name);
    }
}
