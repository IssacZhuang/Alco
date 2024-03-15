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

    #region Binary Serialization

    public byte[] EncodeToBinary()
    {
        BinaryTable table = new BinaryTable
        {
            {"VertexShader", UtilsShaderSerialization.EncodeStageSource(VertexShader)},
            {"FragmentShader", UtilsShaderSerialization.EncodeStageSource(FragmentShader)},
            {"ComputeShader", UtilsShaderSerialization.EncodeStageSource(ComputeShader)},
            {"PreproccessResult", UtilsShaderSerialization.EncodePreproccessResult(PreproccessResult)},
            {"ReflectionInfo", UtilsShaderSerialization.EncodeReflectionInfo(ReflectionInfo)}
        };

        return BinaryParser.EncodeTable(table);
    }

    public static ShaderCompileResult DecodeFromBinary(byte[] bytes)
    {
        BinaryTable table = BinaryParser.DecodeTable(bytes);
        if (table.TryGetBinary("VertexShader", out byte[]? vertexShaderBytes) &&
           table.TryGetBinary("FragmentShader", out byte[]? fragmentShaderBytes) &&
           table.TryGetBinary("ComputeShader", out byte[]? computeShaderBytes) &&
           table.TryGetBinary("PreproccessResult", out byte[]? preproccessResultTable) &&
           table.TryGetBinary("ReflectionInfo", out byte[]? reflectionInfoTable))
        {
            ShaderStageSource? vertexShader = UtilsShaderSerialization.DecodeStageSource(vertexShaderBytes);
            ShaderStageSource? fragmentShader = UtilsShaderSerialization.DecodeStageSource(fragmentShaderBytes);
            ShaderStageSource? computeShader = UtilsShaderSerialization.DecodeStageSource(computeShaderBytes);
            ShaderPreproccessResult preproccessResult = UtilsShaderSerialization.DecodePreproccessResult(preproccessResultTable);
            ShaderReflectionInfo reflectionInfo = UtilsShaderSerialization.DecodeReflectionInfo(reflectionInfoTable);

            return new ShaderCompileResult(vertexShader, fragmentShader, computeShader, preproccessResult, reflectionInfo);
        }

        throw new InvalidOperationException("Invalid shader compile result");

    }

    #endregion
}