using System.Text;
using Alco.Graphics;
using NUnit.Framework;

namespace Alco.Rendering.Test;

/// <summary>Verifies module identity and alias resolution independently of asset enumeration order.</summary>
[TestFixture]
public class TestShaderModuleResolver
{
    /// <summary>Resolves the requested shadow module when similarly named modules appear first.</summary>
    [Test]
    public void Resolve_DoesNotSelectPrefixedModule()
    {
        const string expected = "Shaders/Passes/Compute/ShadowGpuCull.slang";
        SlangFileResolver resolver = CreateResolver(
            "Shaders/Passes/Compute/AmbientShadowGpuCull.slang",
            "Shaders/Passes/Compute/Ambient_ShadowGpuCull.slang",
            expected);

        Assert.That(resolver("ShadowGpuCull.slang"), Is.EqualTo(expected));
    }

    /// <summary>Leaves missing modules unresolved instead of substituting a similar file name.</summary>
    /// <param name="asset">The available shader asset.</param>
    /// <param name="probe">The missing module probe.</param>
    [TestCase("Shaders/AmbientShadowGpuCull.slang", "ShadowGpuCull.slang")]
    [TestCase("Shaders/Ambient_ShadowGpuCull.slang", "ShadowGpuCull.slang")]
    [TestCase("Shaders/ShadowGpuCull.slang", "AmbientShadowGpuCull.slang")]
    public void Resolve_RequiresCompleteModuleName(string asset, string probe)
    {
        SlangFileResolver resolver = CreateResolver(asset);

        Assert.That(resolver(probe), Is.Null);
    }

    /// <summary>Preserves module aliases and relative probes while preferring the complete module name.</summary>
    /// <param name="probe">The compiler's module or relative import probe.</param>
    /// <param name="shorterFirst">Whether the shorter module is enumerated first.</param>
    [TestCase("AlcoRendering_Core.slang", true)]
    [TestCase("AlcoRendering-Core.slang", true)]
    [TestCase("AlcoRendering/Core.slang", true)]
    [TestCase("Shaders/Passes/AlcoRendering_Core.slang", true)]
    [TestCase("Assets/Shaders/Passes/AlcoRendering_Core.slang", true)]
    [TestCase("alcorendering_core.SLANG", true)]
    [TestCase("AlcoRendering_Core.slang", false)]
    public void Resolve_PrefersLongestCompleteModuleName(string probe, bool shorterFirst)
    {
        const string expected = "Shaders/Libs/AlcoRendering_Core.slang";
        const string shorter = "Core.slang";
        SlangFileResolver resolver = shorterFirst
            ? CreateResolver(shorter, expected)
            : CreateResolver(expected, shorter);

        Assert.That(resolver(probe), Is.EqualTo(expected));
    }

    /// <summary>Keeps exact asset paths authoritative when another module has a longer file name.</summary>
    [Test]
    public void Resolve_ExactAssetPathTakesPrecedence()
    {
        const string expected = "AlcoRendering/Core.slang";
        SlangFileResolver resolver = CreateResolver("Shaders/Libs/AlcoRendering_Core.slang", expected);

        Assert.That(resolver(expected), Is.EqualTo(expected));
    }

    /// <summary>Loads built-in modules from deployed content without an engine asset system or resolver.</summary>
    [Test]
    public void RenderingSystem_DefaultResolver_LoadsDeployedShaderModules()
    {
        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem();
        ShaderSystem shaders = host.RenderingSystem.ShaderSystem;
        string libraries = Path.Combine(AppContext.BaseDirectory, "Assets", "Shaders", "Libs");
        string[] files = Directory.GetFiles(libraries, "AlcoRendering_*.slang");
        Assert.That(files, Is.Not.Empty, "Rendering content must be deployed beside the test executable.");

        foreach (string file in files)
        {
            string module = Path.GetFileNameWithoutExtension(file);
            Assert.That(shaders.Modules.GetModuleSource(module), Is.EqualTo(File.ReadAllText(file)), module);
            Assert.That(shaders.GetLibrary(module).Name, Is.EqualTo(module));
        }
        Assert.Multiple(() =>
        {
            Assert.That(shaders.GetShader("Sprite").Name, Is.EqualTo("Sprite"));
            Assert.That(shaders.GetShader("Text").Name, Is.EqualTo("Text"));
        });
    }

    /// <summary>Keeps caller-provided module sources authoritative and isolated from disk fallbacks.</summary>
    [Test]
    public void RenderingSystem_ExplicitResolver_RemainsAuthoritative()
    {
        const string source = "module AlcoRendering_Core; public static const float CallerValue = 7.0;";
        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem(CreateResolverWithSource(source));
        ShaderSystem shaders = host.RenderingSystem.ShaderSystem;

        Assert.Multiple(() =>
        {
            Assert.That(shaders.Modules.GetModuleSource("AlcoRendering_Core"), Is.EqualTo(source));
            Assert.That(shaders.GetLibrary("AlcoRendering_Core").Name, Is.EqualTo("AlcoRendering_Core"));
            Assert.That(shaders.Modules.GetModuleSource("Text"), Is.Null,
                "An explicit virtual file system must not silently fall back to deployed content.");
        });
    }

    /// <summary>Preserves direct disk module discovery and relative imports and includes.</summary>
    [Test]
    public void RenderingSystem_DefaultResolver_PreservesDiskModulesAndImports()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"alco_rendering_resolver_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        const string source = """
            import disk_dependency;
            #include "disk_values.slangh"
            [shader("vertex")]
            float4 MainVS(float3 position : POSITION) : SV_POSITION { return float4(position, 1); }
            [shader("fragment")]
            float4 MainPS() : SV_TARGET { return DiskColor() * DiskValue; }
            """;
        string path = Path.Combine(directory, "disk_entry.slang").Replace('\\', '/');
        File.WriteAllText(path, source);
        File.WriteAllText(Path.Combine(directory, "disk_dependency.slang"),
            "public float4 DiskColor() { return float4(1, 0, 0, 1); }");
        File.WriteAllText(Path.Combine(directory, "disk_values.slangh"), "static const float DiskValue = 1.0;");

        try
        {
            using DummyRenderingSystemHost host = Utility.CreateRenderingSystem();
            ShaderSystem shaders = host.RenderingSystem.ShaderSystem;
            Assert.That(shaders.Modules.GetModuleSource(path[..^".slang".Length]), Is.EqualTo(source));
            Shader shader = shaders.GetShaderFromModule("test_disk_entry", path, source);
            Assert.That(shader.GetShaderModules().IsGraphicsShader, Is.True);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static SlangFileResolver CreateResolverWithSource(string source)
        => ShaderModuleResolver.Create(
            path => path == "AlcoRendering_Core.slang" ? new MemoryStream(Encoding.UTF8.GetBytes(source)) : null,
            () => ["AlcoRendering_Core.slang"]);

    private static SlangFileResolver CreateResolver(params string[] assets)
    {
        return ShaderModuleResolver.Create(
            path =>
            {
                string? asset = Array.Find(assets, candidate =>
                    string.Equals(candidate, path, StringComparison.OrdinalIgnoreCase));
                return asset == null ? null : new MemoryStream(Encoding.UTF8.GetBytes(asset));
            },
            () => assets);
    }
}
