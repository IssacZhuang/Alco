using Alco.Rendering;
using NUnit.Framework;

namespace Alco.Rendering.Test;

/// <summary>
/// Integration tests for bind group validation flowing through <see cref="ShaderUtility.CompileHLSL"/>.
/// These compile real HLSL to SPIR-V via DXC, so the reflection (and thus the validation) is exercised.
/// </summary>
[TestFixture]
public class TestShaderBindGroupValidation
{
    // A graphics shader with a skipped set index: resources at space0 and space2 (space1 missing).
    private const string SkippedGroupShader = @"
struct Vertex { float3 position : POSITION; };
struct PixelInput { float4 position : SV_POSITION; };

cbuffer C0 : register(b0, space0) { float4 v0; };
cbuffer C2 : register(b0, space2) { float4 v2; };

[shader(""vertex"")]
PixelInput MainVS(Vertex input) {
    PixelInput o;
    o.position = float4(input.position, 1.0) + v0 + v2;
    return o;
}

[shader(""pixel"")]
float4 MainPS(PixelInput input) : SV_TARGET {
    return float4(1.0, 1.0, 1.0, 1.0);
}
";

    // A graphics shader with three contiguous sets (space0..space2).
    private const string ThreeContiguousGroupsShader = @"
struct Vertex { float3 position : POSITION; };
struct PixelInput { float4 position : SV_POSITION; };

cbuffer C0 : register(b0, space0) { float4 v0; };
cbuffer C1 : register(b0, space1) { float4 v1; };
cbuffer C2 : register(b0, space2) { float4 v2; };

[shader(""vertex"")]
PixelInput MainVS(Vertex input) {
    PixelInput o;
    o.position = float4(input.position, 1.0) + v0 + v1 + v2;
    return o;
}

[shader(""pixel"")]
float4 MainPS(PixelInput input) : SV_TARGET {
    return float4(1.0, 1.0, 1.0, 1.0);
}
";

    [Test(Description = "A valid contiguous shader compiles without throwing")]
    public void CompileHLSL_ContiguousUnderLimit_DoesNotThrow()
    {
        Assert.DoesNotThrow(() =>
            ShaderUtility.CompileHLSL(ThreeContiguousGroupsShader, "valid_shader", default, 8));
    }

    [Test(Description = "A shader with a skipped group index throws ShaderValidationException")]
    public void CompileHLSL_SkippedGroup_Throws()
    {
        ShaderValidationException ex = Assert.Throws<ShaderValidationException>(
            () => ShaderUtility.CompileHLSL(SkippedGroupShader, "skipped_group_shader", default, 8))!;
        Assert.That(ex.Message, Does.Contain("contiguous"));
    }

    [Test(Description = "A shader with more groups than the limit throws ShaderValidationException")]
    public void CompileHLSL_TooManyGroups_Throws()
    {
        ShaderValidationException ex = Assert.Throws<ShaderValidationException>(
            () => ShaderUtility.CompileHLSL(ThreeContiguousGroupsShader, "too_many_groups_shader", default, 2))!;
        Assert.That(ex.Message, Does.Contain("exceeds the maximum 2"));
    }
}
