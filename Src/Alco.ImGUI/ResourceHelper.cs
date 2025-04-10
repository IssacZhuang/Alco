using System.Reflection;
using System.Text;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.ImGUI;

/// <summary>
/// Helper class for accessing embedded resources in the assembly
/// </summary>
public static class ImGUIResourceHelper
{
    public static Shader GetImGUIShader(RenderingSystem renderingSystem)
    {
        string shaderCode = GetEmbeddedResourceString("ImGui.hlsl");
        return renderingSystem.CreateShader(shaderCode, "ImGui_Embedded", [
            new(){
                Elements = new VertexElement[] {
                    new(0, 0, VertexFormat.Float32x2, "POSITION"),
                    new(1, 8, VertexFormat.Float32x2, "TEXCOORD0"),
                    new(2, 16, VertexFormat.Unorm8x4, "COLOR"),//the imgui vertex use uint as color
                },
                Stride = 20,
                StepMode = VertexStepMode.Vertex,
            }
        ]);
    }
    /// <summary>
    /// Gets the embedded resource content as string
    /// </summary>
    /// <param name="resourceName">Resource name (e.g. "ImGui.hlsl")</param>
    /// <returns>Content of the resource as string</returns>
    public static string GetEmbeddedResourceString(string resourceName)
    {
        var assembly = typeof(ImGUIResourceHelper).Assembly;
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
        var assembly = typeof(ImGUIResourceHelper).Assembly;
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