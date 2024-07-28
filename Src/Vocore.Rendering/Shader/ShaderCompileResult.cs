using Vocore.Graphics;

namespace Vocore.Rendering;

public class ShaderCompileResult
{
    public bool IsGraphicsShader
    {
        get
        {
            return VertexShader.HasValue && FragmentShader.HasValue;
        }
    }

    public bool IsComputeShader
    {
        get
        {
            return ComputeShader.HasValue;
        }
    }
    public ShaderModule? VertexShader { get; }
    public ShaderModule? FragmentShader { get; }
    public ShaderModule? ComputeShader { get; }
    public ShaderPreproccessResult PreproccessResult { get; }
    public ShaderReflectionInfo ReflectionInfo { get; }

    internal ShaderCompileResult(ShaderModule? vertex,
        ShaderModule? fragment,
        ShaderModule? compute,
        ShaderPreproccessResult preproccessResult,
        ShaderReflectionInfo reflectionInfo)
    {
        VertexShader = vertex;
        FragmentShader = fragment;
        ComputeShader = compute;
        PreproccessResult = preproccessResult;
        ReflectionInfo = reflectionInfo;
    }

    public static ShaderCompileResult CreateGraphics(ShaderModule vertex,
        ShaderModule fragment,
        ShaderPreproccessResult preproccessResult,
        ShaderReflectionInfo reflectionInfo)
    {
        return new ShaderCompileResult(vertex, fragment, null, preproccessResult, reflectionInfo);
    }

    public static ShaderCompileResult CreateCompute(ShaderModule compute,
        ShaderPreproccessResult preproccessResult,
        ShaderReflectionInfo reflectionInfo)
    {
        return new ShaderCompileResult(null, null, compute, preproccessResult, reflectionInfo);
    }
}