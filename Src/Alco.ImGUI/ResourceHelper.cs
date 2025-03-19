using System.Reflection;
using System.Text;

namespace Alco.ImGUI;

/// <summary>
/// Helper class for accessing embedded resources in the assembly
/// </summary>
internal static class ResourceHelper
{
    /// <summary>
    /// Gets the embedded resource content as string
    /// </summary>
    /// <param name="resourceName">Resource name (e.g. "ImGui.hlsl")</param>
    /// <returns>Content of the resource as string</returns>
    public static string GetEmbeddedResourceString(string resourceName)
    {
        var assembly = typeof(ResourceHelper).Assembly;
        var fullResourceName = $"{assembly.GetName().Name}.{resourceName}";

        using var stream = assembly.GetManifestResourceStream(fullResourceName);
        if (stream == null)
        {
            throw new ArgumentException($"Resource '{fullResourceName}' not found in assembly '{assembly.GetName().Name}'");
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Gets the embedded resource content as byte array
    /// </summary>
    /// <param name="resourceName">Resource name (e.g. "ImGui.hlsl")</param>
    /// <returns>Content of the resource as byte array</returns>
    public static byte[] GetEmbeddedResourceBytes(string resourceName)
    {
        var assembly = typeof(ResourceHelper).Assembly;
        var fullResourceName = $"{assembly.GetName().Name}.{resourceName}";

        using var stream = assembly.GetManifestResourceStream(fullResourceName);
        if (stream == null)
        {
            throw new ArgumentException($"Resource '{fullResourceName}' not found in assembly '{assembly.GetName().Name}'");
        }

        using var memoryStream = new MemoryStream();
        stream.CopyTo(memoryStream);
        return memoryStream.ToArray();
    }
}