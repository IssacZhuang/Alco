using Vocore.Graphics;

namespace Vocore.Rendering;

public class ShaderCompileResult
{
    public bool IsGraphicsShader
    {
        get
        {
            return VertexShader != null && FragmentShader != null;
        }
    }

    public bool IsComputeShader
    {
        get
        {
            return ComputeShader != null;
        }
    }
    public ShaderStageSource? VertexShader { get; }
    public ShaderStageSource? FragmentShader { get; }
    public ShaderStageSource? ComputeShader { get; }
    public ShaderPreproccessResult PreproccessResult { get; }
    public ShaderReflectionInfo ReflectionInfo { get; }

    internal ShaderCompileResult(ShaderStageSource? vertex,
        ShaderStageSource? fragment,
        ShaderStageSource? compute,
        ShaderPreproccessResult preproccessResult,
        ShaderReflectionInfo reflectionInfo)
    {
        VertexShader = vertex;
        FragmentShader = fragment;
        ComputeShader = compute;
        PreproccessResult = preproccessResult;
        ReflectionInfo = reflectionInfo;
    }

    public static ShaderCompileResult CreateGraphics(ShaderStageSource vertex,
        ShaderStageSource fragment,
        ShaderPreproccessResult preproccessResult,
        ShaderReflectionInfo reflectionInfo)
    {
        return new ShaderCompileResult(vertex, fragment, null, preproccessResult, reflectionInfo);
    }

    public static ShaderCompileResult CreateCompute(ShaderStageSource compute,
        ShaderPreproccessResult preproccessResult,
        ShaderReflectionInfo reflectionInfo)
    {
        return new ShaderCompileResult(null, null, compute, preproccessResult, reflectionInfo);
    }

}