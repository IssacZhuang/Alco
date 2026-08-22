using Alco.Graphics.Spirv;
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
        SpirvModule module = SpirvReader.Parse(spirv);

        // Find the variable ID by name.
        uint variableId = 0;
        bool found = false;
        foreach (KeyValuePair<uint, string> pair in module.Names)
        {
            if (pair.Value == variableName)
            {
                variableId = pair.Key;
                found = true;
                break;
            }
        }

        if (!found)
        {
            return null;
        }

        // Follow: OpVariable → OpTypePointer → OpTypeImage → Depth operand (word[4]).
        SpirvInstruction variable = module.GetInstruction(variableId)
            ?? throw new InvalidOperationException($"Variable %{variableId} not found.");
        uint pointerTypeId = variable[1];
        SpirvInstruction pointerType = module.GetInstruction(pointerTypeId)
            ?? throw new InvalidOperationException($"Pointer type %{pointerTypeId} not found.");
        uint imageTypeId = pointerType[3];
        SpirvInstruction imageType = module.GetInstruction(imageTypeId)
            ?? throw new InvalidOperationException($"Image type %{imageTypeId} not found.");
        return imageType[4];
    }

    /// <summary>Returns the bound word of the module header.</summary>
    private static uint GetBound(byte[] spirv)
    {
        SpirvModule module = SpirvReader.Parse(spirv);
        return module.Bound;
    }

    /// <summary>
    /// Asserts the structural invariant that every OpLoad of the variable references the
    /// same image type as the variable's pointer (required for a valid module).
    /// </summary>
    private static void AssertTypeChainConsistent(byte[] spirv, string variableName)
    {
        SpirvModule module = SpirvReader.Parse(spirv);

        // Find variable by name.
        uint variableId = 0;
        bool found = false;
        foreach (KeyValuePair<uint, string> pair in module.Names)
        {
            if (pair.Value == variableName)
            {
                variableId = pair.Key;
                found = true;
                break;
            }
        }

        Assert.That(found, Is.True, $"Variable '{variableName}' not found in module.");
        SpirvInstruction variable = module.GetInstruction(variableId)!;
        SpirvInstruction pointerType = module.GetInstruction(variable[1])!;
        uint imageTypeId = pointerType[3];

        foreach (SpirvInstruction inst in module.Instructions)
        {
            // OpLoad of the variable must produce the (possibly cloned) image type.
            if ((SpirvOp)inst.OpCode == SpirvOp.Load && inst[3] == variableId)
            {
                Assert.That(inst[1], Is.EqualTo(imageTypeId),
                    $"OpLoad %{inst[2]} does not produce the variable's image type");
            }

            // OpSampledImage wrapping such a load must use a sampled image type built
            // on the same image type.
            if ((SpirvOp)inst.OpCode == SpirvOp.SampledImage && IsLoadOf(module, inst[3], variableId))
            {
                SpirvInstruction sampledImageType = module.GetInstruction(inst[1])!;
                Assert.That(sampledImageType[2], Is.EqualTo(imageTypeId),
                    $"OpSampledImage %{inst[2]} does not use the variable's image type");
            }
        }
    }

    private static bool IsLoadOf(SpirvModule module, uint id, uint variableId)
    {
        SpirvInstruction? load = module.GetInstruction(id);
        return load != null && (SpirvOp)load.OpCode == SpirvOp.Load && load[3] == variableId;
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

        byte[] patched = SpirvDepthTexturePatcher.MarkDepthTexturesByName(spirv, new[] { "_depthTex" });

        Assert.That(GetImageDepthOperand(patched, "_depthTex"), Is.EqualTo(1));
    }

    [Test(Description = "A texture sharing the same OpTypeImage keeps the non-depth type")]
    public void MarkDepthTextures_SharedType_OtherTextureUnaffected()
    {
        byte[] spirv = Compile(SharedTypeShader);

        byte[] patched = SpirvDepthTexturePatcher.MarkDepthTexturesByName(spirv, new[] { "_depthTex" });

        Assert.That(GetImageDepthOperand(patched, "_colorTex"), Is.EqualTo(2));
    }

    [Test(Description = "The variable/load type chain stays consistent after patching")]
    public void MarkDepthTextures_LoadUsage_TypeChainConsistent()
    {
        byte[] spirv = Compile(SharedTypeShader);

        byte[] patched = SpirvDepthTexturePatcher.MarkDepthTexturesByName(spirv, new[] { "_depthTex" });

        AssertTypeChainConsistent(patched, "_depthTex");
        AssertTypeChainConsistent(patched, "_colorTex");
    }

    [Test(Description = "Comparison-sampled depth textures get a cloned OpSampledImage chain")]
    public void MarkDepthTextures_SampledDepthTexture_TypeChainConsistent()
    {
        byte[] spirv = Compile(ComparisonSampledShader);

        byte[] patched = SpirvDepthTexturePatcher.MarkDepthTexturesByName(spirv, new[] { "_shadowMap" });

        Assert.That(GetImageDepthOperand(patched, "_shadowMap"), Is.EqualTo(1));
        AssertTypeChainConsistent(patched, "_shadowMap");
    }

    [Test(Description = "Patching grows the module bound so new ids stay unique")]
    public void MarkDepthTextures_ClonesTypes_BoundIncreased()
    {
        byte[] spirv = Compile(SharedTypeShader);

        byte[] patched = SpirvDepthTexturePatcher.MarkDepthTexturesByName(spirv, new[] { "_depthTex" });

        Assert.That(GetBound(patched), Is.GreaterThan(GetBound(spirv)));
    }

    [Test(Description = "Unknown names are ignored (per-stage modules may lack the texture)")]
    public void MarkDepthTextures_NameNotPresent_ReturnsUnchangedModule()
    {
        byte[] spirv = Compile(SharedTypeShader);

        byte[] patched = SpirvDepthTexturePatcher.MarkDepthTexturesByName(spirv, new[] { "_doesNotExist" });

        Assert.That(patched, Is.EqualTo(spirv));
    }

    [Test(Description = "Garbage input is reported, not silently swallowed")]
    public void MarkDepthTextures_MalformedModule_Throws()
    {
        Assert.Throws<Graphics.ShaderReflectionException>(() =>
            SpirvDepthTexturePatcher.MarkDepthTexturesByName(new byte[] { 1, 2, 3 }, new[] { "_depthTex" }));
    }
}
