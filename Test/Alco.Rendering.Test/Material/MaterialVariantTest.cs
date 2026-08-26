using Alco.Graphics;
using Alco.ShaderCompiler;
#nullable enable

using NUnit.Framework;

namespace Alco.Rendering.Test;

// ─────────────────────────────────────────────────────────────────────────────
// Shader variant model (plan D3 runtime story): one Shader is one module's
// handle; every variant axis is a generic value specialization requested where
// the retired defines used to be — through the specialization arguments of the
// accessor methods (GetShaderModules/GetGraphicsPipeline/GetComputePipelineInfo)
// or the material factories (materials are construction-bound to shader+spec).
// Specializations compile lazily, once per argument set, and cache inside the
// shader. Runs on the NoGPU device; module/reflection level behavior only.
// ─────────────────────────────────────────────────────────────────────────────
[TestFixture]
public class MaterialVariantTest
{
    // A graphics module with one <let Flag> axis: both specializations share the
    // same binding surface, so materials of either variant bind the same slots.
    private const string QuadModule = """
        import alco.rendering.core;

        cbuffer camera : register(b0, space0)
        {
            float4x4 viewProjection;
        };

        cbuffer material : register(b0, space1)
        {
            Texture2D albedo;
            SamplerState linearClamp;
        };

        struct Vertex
        {
            float3 position : POSITION;
            float2 uv       : TEXCOORD0;
        };

        struct V2F
        {
            float4 position : SV_POSITION;
            float2 uv       : TEXCOORD0;
        };

        [shader("vertex")]
        V2F MainVS(Vertex input)
        {
            V2F output;
            output.position = mul(viewProjection, float4(input.position, 1.0));
            output.uv = input.uv;
            return output;
        }

        [shader("fragment")]
        float4 MainPS<let Flag : int>(V2F input) : SV_TARGET
        {
            float dither = outputDither8Bit(input.position.xy) * 0.0;
            float4 color = sampleTex2D(albedo, linearClamp, input.uv) + dither + PI * 0.0;
            if (Flag != 0) { color.g += 0.0; }
            return color;
        }
        """;

    // A compute module with the same <let Flag> axis for the compute side.
    private const string ComputeModule = """
        import alco.rendering.core;

        cbuffer pass : register(b0, space0)
        {
            RWStructuredBuffer<float4> output;
        };

        cbuffer material : register(b0, space1)
        {
            Texture2D source;
        };

        [shader("compute")]
        void MainCS<let Flag : int>(uint3 dispatchThreadID : SV_DispatchThreadID)
        {
            // Compute cannot use implicit derivatives — Load instead of Sample.
            float4 value = source.Load(int3(dispatchThreadID.xy, 0));
            if (Flag != 0) { value.g += 0.0; }
            output[dispatchThreadID.x] = value;
        }
        """;

    private static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Alco.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static SlangCompilerOptions Options()
    {
        string corePath = Path.Combine(
            RepoRoot(), "Src", "Alco.Rendering", "Assets", "Shaders", "Libs", "alco-rendering-core.slang");
        string core = File.ReadAllText(corePath);
        return new SlangCompilerOptions
        {
            Resolver = path =>
            {
                string key = SlangPathUtility.NormalizePath(path).Replace('/', '-');
                if (key.EndsWith("alco-rendering-core.slang", StringComparison.OrdinalIgnoreCase))
                    return core;
                if (key.EndsWith("test-variant-quad.slang", StringComparison.OrdinalIgnoreCase))
                    return QuadModule;
                if (key.EndsWith("test-variant-compute.slang", StringComparison.OrdinalIgnoreCase))
                    return ComputeModule;
                return null;
            },
        };
    }

    [Test]
    public void Shader_CachesModulesPerSpecialization()
    {
        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem();
        using ShaderSystem shaderSystem = new(host.RenderingSystem, Options(), cacheDirectory: null);

        Shader shader = shaderSystem.GetShader("test-variant-quad");
        ShaderModulesInfo variant0 = shader.GetShaderModules(0);
        ShaderModulesInfo variant1 = shader.GetShaderModules(1);

        Assert.Multiple(() =>
        {
            Assert.That(shader.GetShaderModules(0), Is.SameAs(variant0), "specializations cache inside the shader");
            Assert.That(shader.GetShaderModules(1), Is.SameAs(variant1));
            Assert.That(variant0, Is.Not.SameAs(variant1),
                "each specialization is its own compiled entry");
            // The module's entry points are generic (<let Flag>): it cannot link
            // unspecialized — only its argument sets are valid requests.
        });
    }

    [Test]
    public void Graphics_MaterialIsConstructionBoundToTheSpecialization()
    {
        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem();
        using ShaderSystem shaderSystem = new(host.RenderingSystem, Options(), cacheDirectory: null);

        Shader shader = shaderSystem.GetShader("test-variant-quad");
        ShaderModulesInfo variant1 = shader.GetShaderModules(1);

        // The variant pins at construction: no runtime switching surface exists.
        using GraphicsMaterial material = host.RenderingSystem.CreateGraphicsMaterial(shader, "variant_material", 1);

        Assert.Multiple(() =>
        {
            Assert.That(material.Shader, Is.SameAs(shader), "the handle is shared across variants");
            Assert.That(material.Specializations, Is.EqualTo(new[] { "1" }));
            Assert.That(material.ReflectionInfo, Is.SameAs(variant1.ReflectionInfo),
                "the material reflects its pinned variant");
        });

        // A second material of the other variant shares the handle, not the modules.
        using GraphicsMaterial other = host.RenderingSystem.CreateGraphicsMaterial(shader, "other_material", 0);
        Assert.That(other.Specializations, Is.EqualTo(new[] { "0" }));
    }

    [Test]
    public void Graphics_Instance_InheritsTheParentSpecialization()
    {
        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem();
        using ShaderSystem shaderSystem = new(host.RenderingSystem, Options(), cacheDirectory: null);

        using GraphicsMaterial material = host.RenderingSystem.CreateGraphicsMaterial(
            shaderSystem.GetShader("test-variant-quad"), "variant_material", 1);
        GraphicsMaterialInstance instance = material.CreateInstance();

        Assert.That(instance.Specializations, Is.EqualTo(new[] { "1" }),
            "instances resolve slots from their pinned parent variant");
    }

    [Test]
    public void Graphics_Material_RejectsComputeShaders()
    {
        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem();
        using ShaderSystem shaderSystem = new(host.RenderingSystem, Options(), cacheDirectory: null);

        Shader compute = shaderSystem.GetShader("test-variant-compute");

        Assert.That(() =>
        {
            using GraphicsMaterial material = host.RenderingSystem.CreateGraphicsMaterial(
                compute, "variant_material", 0);
        }, Throws.InvalidOperationException);
    }

    [Test]
    public void Compute_MaterialIsConstructionBoundToTheSpecialization()
    {
        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem();
        using ShaderSystem shaderSystem = new(host.RenderingSystem, Options(), cacheDirectory: null);

        Shader shader = shaderSystem.GetShader("test-variant-compute");
        ShaderModulesInfo variant1 = shader.GetShaderModules(1);

        using ComputeMaterial material = host.RenderingSystem.CreateComputeMaterial(shader, 1);
        ComputeMaterialInstance instance = material.CreateInstance();

        Assert.Multiple(() =>
        {
            Assert.That(material.Specializations, Is.EqualTo(new[] { "1" }));
            Assert.That(material.ReflectionInfo, Is.SameAs(variant1.ReflectionInfo));
            Assert.That(instance.Specializations, Is.EqualTo(new[] { "1" }));
        });
    }

    [Test]
    public void ShaderSystem_InternsHandlesPerModule()
    {
        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem();
        using ShaderSystem shaderSystem = new(host.RenderingSystem, Options(), cacheDirectory: null);

        Assert.That(shaderSystem.GetShader("test-variant-quad"),
            Is.SameAs(shaderSystem.GetShader("test-variant-quad")),
            "one handle per module — variants live inside it, cached per specialization");
    }
}
