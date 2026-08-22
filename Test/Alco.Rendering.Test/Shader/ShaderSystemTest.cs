using Alco.Graphics;
using Alco.ShaderCompiler;
#nullable enable

using NUnit.Framework;

namespace Alco.Rendering.Test;

// ─────────────────────────────────────────────────────────────────────────────
// Phase-1 ShaderSystem tests (plan §4.2): module-name keyed Shader creation
// through the provider seam, specialization identity, and hot-reload
// invalidation wired to Shader version bumps. Runs on the NoGPU device; only
// module/reflection level behavior is asserted (pipelines need a real device).
// ─────────────────────────────────────────────────────────────────────────────
[TestFixture]
public class ShaderSystemTest
{
    private const string QuadModule = """
        import alco.rendering.core;

        cbuffer _camera : register(b0, space0)
        {
            float4x4 viewProjection;
        };

        Texture2D _albedo        : register(t0, space1);
        SamplerState _albedoSampler : register(s0, space1);

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
        float4 MainPS(V2F input) : SV_TARGET
        {
            // Prove the import graph is live: core's helper and constant.
            float dither = OutputDither8Bit(input.position.xy) * 0.0;
            return SampleTex2D(_albedo, _albedoSampler, input.uv) + dither + PI * 0.0;
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

    private static SlangCompilerOptions OptionsWithQuadModule()
    {
        string corePath = Path.Combine(
            RepoRoot(), "Src", "Alco.Rendering", "Assets", "ShadersSlang", "Libs", "alco-rendering-core.slang");
        string core = File.ReadAllText(corePath);
        return new SlangCompilerOptions
        {
            Resolver = path =>
            {
                // Imports probe several module-name→file forms ('a/b.slang',
                // 'a-b.slang', …); match on the dashed form.
                string key = SlangPathUtility.NormalizePath(path).Replace('/', '-');
                if (key.EndsWith("alco-rendering-core.slang", StringComparison.OrdinalIgnoreCase))
                    return core;
                if (key.EndsWith("alco-sandbox-quad.slang", StringComparison.OrdinalIgnoreCase))
                    return QuadModule;
                return null;
            },
        };
    }

    [Test]
    public void GetShader_CompilesModuleWithImports()
    {
        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem();
        using ShaderSystem shaderSystem = new(host.RenderingSystem, OptionsWithQuadModule(), cacheDirectory: null);

        Shader shader = shaderSystem.GetShader("alco-sandbox-quad");
        ShaderModulesInfo modules = shader.GetShaderModules();

        Assert.Multiple(() =>
        {
            Assert.That(shader.IsComputeShader, Is.False);
            Assert.That(modules.IsGraphicsShader, Is.True);
            Assert.That(modules.VertexShader, Is.Not.Null);
            Assert.That(modules.FragmentShader, Is.Not.Null);
            Assert.That(modules.VertexShader!.Value.Source.Length, Is.GreaterThan(4));
            // Name-based binding surface survives (D1): the engine resolves by name.
            Assert.That(modules.ReflectionInfo.TryGetResourceId("_camera", out _), Is.True);
            Assert.That(modules.ReflectionInfo.TryGetResourceId("_albedo", out _), Is.True);
            Assert.That(modules.ReflectionInfo.VertexLayouts.Count, Is.EqualTo(1));
            // The core module is part of the dependency graph.
            Assert.That(shaderSystem.Modules.GetModuleDependencies("alco-sandbox-quad"),
                Has.Some.Contains("core.slang"));
        });
    }

    [Test]
    public void GetShader_CachesPerModuleAndSpecialization()
    {
        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem();
        using ShaderSystem shaderSystem = new(host.RenderingSystem, OptionsWithQuadModule(), cacheDirectory: null);

        Shader a = shaderSystem.GetShader("alco-sandbox-quad");
        Shader b = shaderSystem.GetShader("alco-sandbox-quad");

        Assert.That(b, Is.SameAs(a), "same (module, specialization) must return the same Shader");
    }

    [Test]
    public void Invalidate_CoreModule_BumpsShaderVersionAndFiresEvent()
    {
        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem();
        using ShaderSystem shaderSystem = new(host.RenderingSystem, OptionsWithQuadModule(), cacheDirectory: null);
        Shader shader = shaderSystem.GetShader("alco-sandbox-quad");
        uint versionBefore = shader.Version;
        List<Shader> invalidated = [];
        shaderSystem.ShaderInvalidated += invalidated.Add;

        string coreDep = shaderSystem.Modules.GetModuleDependencies("alco-sandbox-quad")
            .First(dep => dep.Contains("core.slang"));
        IReadOnlyList<string> affected = shaderSystem.InvalidateModulesContaining(coreDep);

        Assert.Multiple(() =>
        {
            Assert.That(affected, Is.EqualTo(new[] { "alco-sandbox-quad" }));
            Assert.That(invalidated, Is.EqualTo(new[] { shader }));
            Assert.That(shader.Version, Is.GreaterThan(versionBefore), "version must bump for lazy pipeline rebuild");
            // The shader instance stays alive; its caches were cleared, so the
            // next use recompiles through the (fresh) module system.
            Assert.That(shaderSystem.GetShader("alco-sandbox-quad"), Is.SameAs(shader));
            Assert.DoesNotThrow(() => _ = shader.GetShaderModules());
        });
    }
}
