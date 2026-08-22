using Alco.Graphics;
using Alco.Rendering;
using Alco.ShaderCompiler;
using NUnit.Framework;

namespace Alco.Rendering.Test;

/// <summary>
/// Integration tests for bind group validation on the slang module path:
/// real modules compile through the slang toolchain, so set contiguity (enforced
/// by the reflection reader) and the device bind group limit (enforced by
/// <see cref="ShaderSystem"/> via <see cref="ShaderReflectionUtility.ValidateBindGroupLayouts"/>)
/// are both exercised against actual reflection output.
/// </summary>
[TestFixture]
public class TestShaderBindGroupValidation
{
    // A graphics module with a skipped set index: resources at set 0 and set 2 (set 1 missing).
    private const string SkippedGroupShader = """
        module skipped_group_shader;

        [[vk::binding(0, 0)]] cbuffer C0 { float4 v0; };
        [[vk::binding(0, 2)]] cbuffer C2 { float4 v2; };

        struct Vertex { float3 position : POSITION; };
        struct V2F { float4 position : SV_POSITION; };

        [shader("vertex")]
        V2F MainVS(Vertex input)
        {
            V2F o;
            o.position = float4(input.position, 1.0) + v0 + v2;
            return o;
        }

        [shader("pixel")]
        float4 MainPS(V2F input) : SV_TARGET
        {
            return float4(1.0, 1.0, 1.0, 1.0);
        }
        """;

    // A graphics module with three contiguous sets (0..2).
    private const string ThreeContiguousGroupsShader = """
        module three_contiguous_groups_shader;

        [[vk::binding(0, 0)]] cbuffer C0 { float4 v0; };
        [[vk::binding(0, 1)]] cbuffer C1 { float4 v1; };
        [[vk::binding(0, 2)]] cbuffer C2 { float4 v2; };

        struct Vertex { float3 position : POSITION; };
        struct V2F { float4 position : SV_POSITION; };

        [shader("vertex")]
        V2F MainVS(Vertex input)
        {
            V2F o;
            o.position = float4(input.position, 1.0) + v0 + v1 + v2;
            return o;
        }

        [shader("pixel")]
        float4 MainPS(V2F input) : SV_TARGET
        {
            return float4(1.0, 1.0, 1.0, 1.0);
        }
        """;

    // A module with nine sets (0..8), one cbuffer each — over the NoGPU device's limit of 8.
    private const string TooManyGroupsShader = """
        module too_many_groups_shader;

        [[vk::binding(0, 0)]] cbuffer C0 { float4 v0; };
        [[vk::binding(0, 1)]] cbuffer C1 { float4 v1; };
        [[vk::binding(0, 2)]] cbuffer C2 { float4 v2; };
        [[vk::binding(0, 3)]] cbuffer C3 { float4 v3; };
        [[vk::binding(0, 4)]] cbuffer C4 { float4 v4; };
        [[vk::binding(0, 5)]] cbuffer C5 { float4 v5; };
        [[vk::binding(0, 6)]] cbuffer C6 { float4 v6; };
        [[vk::binding(0, 7)]] cbuffer C7 { float4 v7; };
        [[vk::binding(0, 8)]] cbuffer C8 { float4 v8; };

        struct Vertex { float3 position : POSITION; };
        struct V2F { float4 position : SV_POSITION; };

        [shader("vertex")]
        V2F MainVS(Vertex input)
        {
            V2F o;
            o.position = float4(input.position, 1.0)
                + v0 + v1 + v2 + v3 + v4 + v5 + v6 + v7 + v8;
            return o;
        }

        [shader("pixel")]
        float4 MainPS(V2F input) : SV_TARGET
        {
            return float4(1.0, 1.0, 1.0, 1.0);
        }
        """;

    private static void CompileSource(string moduleName, string source)
    {
        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem();
        using ShaderSystem shaderSystem = new(
            host.RenderingSystem, new SlangCompilerOptions { Resolver = _ => null }, cacheDirectory: null);
        shaderSystem.GetShaderFromModule(moduleName, $"{moduleName}.slang", source);
    }

    [Test(Description = "A valid contiguous module compiles without throwing")]
    public void CompileSlang_ContiguousUnderLimit_DoesNotThrow()
    {
        Assert.DoesNotThrow(() =>
            CompileSource("valid_shader", ThreeContiguousGroupsShader));
    }

    [Test(Description = "A module with a skipped set index is rejected by the reflection reader")]
    public void CompileSlang_SkippedGroup_Throws()
    {
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => CompileSource("skipped_group_shader", SkippedGroupShader))!;
        Assert.That(ex.Message, Does.Contain("non-contiguous set 2"));
    }

    [Test(Description = "A module with more sets than the device limit throws ShaderReflectionException")]
    public void CompileSlang_TooManyGroups_Throws()
    {
        ShaderReflectionException ex = Assert.Throws<ShaderReflectionException>(
            () => CompileSource("too_many_groups_shader", TooManyGroupsShader))!;
        Assert.That(ex.Message, Does.Contain("exceeds the maximum 8"));
    }
}
