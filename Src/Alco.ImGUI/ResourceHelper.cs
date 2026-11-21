using System.Reflection;
using System.Text;
using Alco.Graphics;

namespace Alco.ImGUI;

/// <summary>
/// Helper class for accessing embedded resources in the assembly.
/// </summary>
public static class ImGUIResourceHelper
{
    /// <summary>Gets or compiles the shared shader used to render ImGui geometry.</summary>
    /// <param name="shaderSystem">The shader system that compiles and caches the shader.</param>
    /// <returns>The compiled ImGui shader.</returns>
    public static Shader GetImGUIShader(ShaderSystem shaderSystem)
    {
        ArgumentNullException.ThrowIfNull(shaderSystem);
        string source = GetEmbeddedResourceString("ImGUI.slang");
        return shaderSystem.GetShaderFromSource("ImGUI", "ImGUI.slang", source, ShaderKind.Graphics, "ImGUI");
    }

    /// <summary>
    /// Gets the embedded resource content as a UTF-8 string.
    /// </summary>
    /// <param name="resourceName">Resource name (e.g. "ImGUI.slang").</param>
    /// <returns>Content of the resource as a string.</returns>
    public static string GetEmbeddedResourceString(string resourceName)
    {
        Assembly assembly = typeof(ImGUIResourceHelper).Assembly;
        string fullResourceName = $"{assembly.GetName().Name}.{resourceName}";

        using Stream stream = assembly.GetManifestResourceStream(fullResourceName)
            ?? throw new ArgumentException($"Resource '{fullResourceName}' not found in assembly '{assembly.GetName().Name}'");
        using StreamReader reader = new(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Gets the embedded resource content as a byte array.
    /// </summary>
    /// <param name="resourceName">Resource name (e.g. "ImGUI.slang").</param>
    /// <returns>Content of the resource as a byte array.</returns>
    public static byte[] GetEmbeddedResourceBytes(string resourceName)
    {
        Assembly assembly = typeof(ImGUIResourceHelper).Assembly;
        string fullResourceName = $"{assembly.GetName().Name}.{resourceName}";

        using Stream stream = assembly.GetManifestResourceStream(fullResourceName)
            ?? throw new ArgumentException($"Resource '{fullResourceName}' not found in assembly '{assembly.GetName().Name}'");
        using MemoryStream memoryStream = new();
        stream.CopyTo(memoryStream);
        return memoryStream.ToArray();
    }
}
