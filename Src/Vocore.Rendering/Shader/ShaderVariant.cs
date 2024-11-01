using Vocore.Graphics;

namespace Vocore.Rendering;

public class ShaderVariant
{
    public string[] Defines { get; }
    public ShaderModule? VertexShader { get; }
    public ShaderModule? FragmentShader { get; }
    public ShaderModule? ComputeShader { get; }

    public ShaderReflectionInfo ReflectionInfo { get; }

    public ShaderVariant(
        string[] defines,
        ShaderModule? vertex,
        ShaderModule? fragment,
        ShaderModule? compute,
        ShaderReflectionInfo reflectionInfo)
    {
        Defines = defines;
        VertexShader = vertex;
        FragmentShader = fragment;
        ComputeShader = compute;
        ReflectionInfo = reflectionInfo;
    }
}