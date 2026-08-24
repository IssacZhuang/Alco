using System.Collections.Frozen;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Represents the compiled modules of one shader variant (a Shader is exactly
/// one specialization; variants are distinct Shader objects).
/// </summary>
public sealed class ShaderModulesInfo
{
    public string Name { get; }

    /// <summary>
    /// The vertex shader module.
    /// </summary>
    public ShaderModule? VertexShader { get; }

    /// <summary>
    /// The fragment shader module.
    /// </summary>
    public ShaderModule? FragmentShader { get; }

    /// <summary>
    /// The compute shader module.
    /// </summary>
    public ShaderModule? ComputeShader { get; }

    /// <summary>
    /// The reflection information for the shader.
    /// </summary>
    public ShaderReflectionInfo ReflectionInfo { get; }

    public bool IsGraphicsShader => VertexShader.HasValue && FragmentShader.HasValue;
    public bool IsComputeShader => ComputeShader.HasValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShaderModulesInfo"/> class.
    /// </summary>
    /// <param name="vertex">The vertex shader module.</param>
    /// <param name="fragment">The fragment shader module.</param>
    /// <param name="compute">The compute shader module.</param>
    /// <param name="reflectionInfo">The reflection information for the shader.</param>
    public ShaderModulesInfo(
        string name,
        ShaderModule? vertex,
        ShaderModule? fragment,
        ShaderModule? compute,
        ShaderReflectionInfo reflectionInfo)
    {
        Name = name;
        VertexShader = vertex;
        FragmentShader = fragment;
        ComputeShader = compute;
        ReflectionInfo = reflectionInfo;
    }

    /// <summary>
    /// Creates a new graphics shader variant with the specified vertex and fragment shaders.
    /// </summary>
    /// <param name="vertexShader">The vertex shader module.</param>
    /// <param name="fragmentShader">The fragment shader module.</param>
    /// <param name="reflectionInfo">The reflection information for the shader.</param>
    /// <returns>A new <see cref="ShaderModulesInfo"/> instance configured for graphics.</returns>
    public static ShaderModulesInfo CreateGraphics(
        string name,
        ShaderModule vertexShader,
        ShaderModule fragmentShader,
        ShaderReflectionInfo reflectionInfo)
    {
        return new ShaderModulesInfo(name, vertexShader, fragmentShader, null, reflectionInfo);
    }

    /// <summary>
    /// Creates a new compute shader variant with the specified compute shader.
    /// </summary>
    /// <param name="computeShader">The compute shader module.</param>
    /// <param name="reflectionInfo">The reflection information for the shader.</param>
    /// <returns>A new <see cref="ShaderModulesInfo"/> instance configured for compute.</returns>
    public static ShaderModulesInfo CreateCompute(
        string name,
        ShaderModule computeShader,
        ShaderReflectionInfo reflectionInfo)
    {
        return new ShaderModulesInfo(name, null, null, computeShader, reflectionInfo);
    }

}