using NUnit.Framework;

namespace Alco.ShaderCompiler;

/// <summary>
/// Unit tests for <see cref="SpirvDepthTexturePatcher"/>. Compiles real HLSL through DXC
/// and verifies the rewriter marks only the requested texture as a depth image
/// (OpTypeImage Depth operand = 1) while keeping the module structurally consistent
/// (variable / load / sampled-image type chain).
/// </summary>
[TestFixture]
public class SpirvDepthTexturePatcherTests
{
    // Two textures with the same HLSL signature: DXC shares one OpTypeImage between
    // them, so the patcher must clone the type chain instead of mutating in place.
    private const string SharedTypeShader = @"
[[vk::binding(0, 0)]] Texture2D<float> _depthTex;
[[vk::binding(1, 0)]] Texture2D<float> _colorTex;
[[vk::binding(1, 1)]] SamplerState _colorTexSampler;

float4 main(float4 pos : SV_POSITION) : SV_TARGET
{
    float d = _depthTex.Load(int3(0, 0, 0));
    float c = _colorTex.Sample(_colorTexSampler, float2(0.5, 0.5));
    return float4(c, d, 0, 1);
}";

    // The depth texture is also sampled (comparison sampler), so the OpSampledImage
    // type chain has to be cloned as well.
    private const string ComparisonSampledShader = @"
[[vk::binding(0, 0)]] Texture2D<float> _shadowMap;
[[vk::binding(0, 1)]] SamplerComparisonState _shadowMapSampler;

float4 main(float4 pos : SV_POSITION) : SV_TARGET
{
    float s = _shadowMap.SampleCmpLevelZero(_shadowMapSampler, float2(0.5, 0.5), 0.5);
    return float4(s, 0, 0, 1);
}";

    private static byte[] Compile(string hlsl)
    {
        return ShaderCompilerDxc.ConvetHlslToSpirv(
            hlsl,
            "test.hlsl",
            "main",
            Graphics.ShaderStage.Fragment,
            Span<ShaderMacroDefine>.Empty);
    }

    /// <summary>
    /// Follows the type chain of a named global variable and returns the Depth operand
    /// of its OpTypeImage, or null when the variable is not present.
    /// </summary>
    private static uint? GetImageDepthOperand(byte[] spirv, string variableName)
    {
        uint[] words = ToWords(spirv);
        foreach ((uint[] instruction, int _) in EnumerateInstructions(words))
        {
            // OpName
            if (Opcode(instruction) != 5 || ReadString(instruction, 2) != variableName)
            {
                continue;
            }

            uint variableId = instruction[1];
            uint pointerTypeId = FindInstruction(words, 59, i => i[2] == variableId)![1];
            uint imageTypeId = FindInstruction(words, 32, i => i[1] == pointerTypeId)![3];
            uint[] image = FindInstruction(words, 25, i => i[1] == imageTypeId)!;
            return image[4];
        }

        return null;
    }

    /// <summary>
    /// Returns the bound word of the module header.
    /// </summary>
    private static uint GetBound(byte[] spirv)
    {
        return ToWords(spirv)[3];
    }

    /// <summary>
    /// Asserts the structural invariant that every OpLoad of the variable references the
    /// same image type as the variable's pointer (required for a valid module).
    /// </summary>
    private static void AssertTypeChainConsistent(byte[] spirv, string variableName)
    {
        uint[] words = ToWords(spirv);

        uint variableId = 0;
        foreach ((uint[] instruction, int _) in EnumerateInstructions(words))
        {
            if (Opcode(instruction) == 5 && ReadString(instruction, 2) == variableName)
            {
                variableId = instruction[1];
                break;
            }
        }

        uint[] variable = FindInstruction(words, 59, i => i[2] == variableId)!;
        uint imageTypeId = FindInstruction(words, 32, i => i[1] == variable[1])![3];

        foreach ((uint[] instruction, int _) in EnumerateInstructions(words))
        {
            // OpLoad of the variable must produce the (possibly cloned) image type.
            if (Opcode(instruction) == 61 && instruction[3] == variableId)
            {
                Assert.That(instruction[1], Is.EqualTo(imageTypeId),
                    $"OpLoad %{instruction[2]} does not produce the variable's image type");
            }

            // OpSampledImage wrapping such a load must use a sampled image type built
            // on the same image type.
            if (Opcode(instruction) == 86 && IsLoadOf(words, instruction[3], variableId))
            {
                uint[] sampledImageType = FindInstruction(words, 27, i => i[1] == instruction[1])!;
                Assert.That(sampledImageType[2], Is.EqualTo(imageTypeId),
                    $"OpSampledImage %{instruction[2]} does not use the variable's image type");
            }
        }
    }

    private static bool IsLoadOf(uint[] words, uint id, uint variableId)
    {
        uint[]? load = FindInstruction(words, 61, i => i[2] == id);
        return load != null && load[3] == variableId;
    }

    private static ushort Opcode(uint[] instruction) => (ushort)(instruction[0] & 0xFFFF);

    private static uint[] ToWords(byte[] spirv)
    {
        uint[] words = new uint[spirv.Length / 4];
        Buffer.BlockCopy(spirv, 0, words, 0, spirv.Length);
        return words;
    }

    private static IEnumerable<(uint[] instruction, int offset)> EnumerateInstructions(uint[] words)
    {
        int offset = 5;
        while (offset < words.Length)
        {
            int wordCount = (int)(words[offset] >> 16);
            uint[] instruction = new uint[wordCount];
            Array.Copy(words, offset, instruction, 0, wordCount);
            yield return (instruction, offset);
            offset += wordCount;
        }
    }

    private static uint[]? FindInstruction(uint[] words, ushort opcode, Func<uint[], bool> predicate)
    {
        foreach ((uint[] instruction, int _) in EnumerateInstructions(words))
        {
            if (Opcode(instruction) == opcode && predicate(instruction))
            {
                return instruction;
            }
        }

        return null;
    }

    private static string ReadString(uint[] instruction, int wordOffset)
    {
        int byteCount = (instruction.Length - wordOffset) * 4;
        byte[] bytes = new byte[byteCount];
        Buffer.BlockCopy(instruction, wordOffset * 4, bytes, 0, byteCount);
        int length = Array.IndexOf<byte>(bytes, 0);
        return System.Text.Encoding.UTF8.GetString(bytes, 0, length < 0 ? bytes.Length : length);
    }

    [Test(Description = "DXC emits the Depth operand as 2 (unknown) for all textures")]
    public void Compile_DxcOutput_DepthOperandIsUnknown()
    {
        byte[] spirv = Compile(SharedTypeShader);

        Assert.That(GetImageDepthOperand(spirv, "_depthTex"), Is.EqualTo(2));
        Assert.That(GetImageDepthOperand(spirv, "_colorTex"), Is.EqualTo(2));
    }

    [Test(Description = "Marking a texture rewrites it to a depth image (Depth = 1)")]
    public void MarkDepthTextures_TargetTexture_BecomesDepthImage()
    {
        byte[] spirv = Compile(SharedTypeShader);

        byte[] patched = SpirvDepthTexturePatcher.MarkDepthTextures(spirv, new[] { "_depthTex" });

        Assert.That(GetImageDepthOperand(patched, "_depthTex"), Is.EqualTo(1));
    }

    [Test(Description = "A texture sharing the same OpTypeImage keeps the non-depth type")]
    public void MarkDepthTextures_SharedType_OtherTextureUnaffected()
    {
        byte[] spirv = Compile(SharedTypeShader);

        byte[] patched = SpirvDepthTexturePatcher.MarkDepthTextures(spirv, new[] { "_depthTex" });

        Assert.That(GetImageDepthOperand(patched, "_colorTex"), Is.EqualTo(2));
    }

    [Test(Description = "The variable/load type chain stays consistent after patching")]
    public void MarkDepthTextures_LoadUsage_TypeChainConsistent()
    {
        byte[] spirv = Compile(SharedTypeShader);

        byte[] patched = SpirvDepthTexturePatcher.MarkDepthTextures(spirv, new[] { "_depthTex" });

        AssertTypeChainConsistent(patched, "_depthTex");
        AssertTypeChainConsistent(patched, "_colorTex");
    }

    [Test(Description = "Comparison-sampled depth textures get a cloned OpSampledImage chain")]
    public void MarkDepthTextures_SampledDepthTexture_TypeChainConsistent()
    {
        byte[] spirv = Compile(ComparisonSampledShader);

        byte[] patched = SpirvDepthTexturePatcher.MarkDepthTextures(spirv, new[] { "_shadowMap" });

        Assert.That(GetImageDepthOperand(patched, "_shadowMap"), Is.EqualTo(1));
        AssertTypeChainConsistent(patched, "_shadowMap");
    }

    [Test(Description = "Patching grows the module bound so new ids stay unique")]
    public void MarkDepthTextures_ClonesTypes_BoundIncreased()
    {
        byte[] spirv = Compile(SharedTypeShader);

        byte[] patched = SpirvDepthTexturePatcher.MarkDepthTextures(spirv, new[] { "_depthTex" });

        Assert.That(GetBound(patched), Is.GreaterThan(GetBound(spirv)));
    }

    [Test(Description = "Unknown names are ignored (per-stage modules may lack the texture)")]
    public void MarkDepthTextures_NameNotPresent_ReturnsUnchangedModule()
    {
        byte[] spirv = Compile(SharedTypeShader);

        byte[] patched = SpirvDepthTexturePatcher.MarkDepthTextures(spirv, new[] { "_doesNotExist" });

        Assert.That(patched, Is.EqualTo(spirv));
    }

    [Test(Description = "Garbage input is reported, not silently swallowed")]
    public void MarkDepthTextures_MalformedModule_Throws()
    {
        Assert.Throws<ShaderCompilationException>(() =>
            SpirvDepthTexturePatcher.MarkDepthTextures(new byte[] { 1, 2, 3 }, new[] { "_depthTex" }));
    }
}
