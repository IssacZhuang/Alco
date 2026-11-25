using System.Text;
using Alco.Graphics;
using NUnit.Framework;

namespace Alco.Rendering.Test;

/// <summary>Verifies module identity, alias resolution, and compiler integration for <see cref="ShaderModuleResolver"/>.</summary>
[TestFixture]
public class TestShaderModuleResolver
{
    /// <summary>Resolves the requested shadow module when similarly named modules appear first.</summary>
    [Test]
    public void Resolve_DoesNotSelectPrefixedModule()
    {
        const string expected = "Shaders/Passes/Compute/ShadowGpuCull.slang";
        ShaderFileResolver resolver = CreateResolver(
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
        ShaderFileResolver resolver = CreateResolver(asset);

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
        ShaderFileResolver resolver = shorterFirst
            ? CreateResolver(shorter, expected)
            : CreateResolver(expected, shorter);

        Assert.That(resolver(probe), Is.EqualTo(expected));
    }

    /// <summary>Keeps exact asset paths authoritative when another module has a longer file name.</summary>
    [Test]
    public void Resolve_ExactAssetPathTakesPrecedence()
    {
        const string expected = "AlcoRendering/Core.slang";
        ShaderFileResolver resolver = CreateResolver("Shaders/Libs/AlcoRendering_Core.slang", expected);

        Assert.That(resolver(expected), Is.EqualTo(expected));
    }

    /// <summary>Resolves built-in deployed shader modules from output Assets without engine wiring.</summary>
    [Test]
    public void Resolver_DeployedShaderAssets_ResolvesLibsAndPasses()
    {
        string assetsRoot = Path.Combine(AppContext.BaseDirectory, "Assets");
        string shaderRoot = Path.Combine(assetsRoot, "Shaders");
        string[] files = Directory.GetFiles(shaderRoot, "*.slang", SearchOption.AllDirectories);
        Assert.That(files, Is.Not.Empty, "Rendering shader assets must be deployed beside the test executable.");

        string[] relativeAssets = files
            .Select(path => Path.GetRelativePath(assetsRoot, path).Replace('\\', '/'))
            .ToArray();
        ShaderFileResolver resolver = ShaderModuleResolver.Create(
            path =>
            {
                string candidate = Path.Combine(assetsRoot, ShaderModuleResolver.NormalizePath(path));
                return File.Exists(candidate) ? File.OpenRead(candidate) : null;
            },
            () => relativeAssets);

        string libsDir = Path.Combine(shaderRoot, "Libs");
        foreach (string libFile in Directory.GetFiles(libsDir, "AlcoRendering_*.slang"))
        {
            string moduleFile = Path.GetFileName(libFile);
            Assert.That(resolver(moduleFile), Is.EqualTo(File.ReadAllText(libFile)), moduleFile);
        }

        Assert.Multiple(() =>
        {
            Assert.That(resolver("Sprite.slang"), Is.Not.Null.And.Not.Empty);
            Assert.That(resolver("Text.slang"), Is.Not.Null.And.Not.Empty);
        });
    }

    /// <summary>Verifies that an explicit <see cref="ShaderFileResolver"/> overrides module resolution in <see cref="ShaderSystem"/>.</summary>
    [Test]
    public void ShaderSystem_ExplicitResolver_RemainsAuthoritative()
    {
        const string source = """
            module VirtualTestModule;
            import Core;

            [GPURoot]
            struct DrawRoot
            {
                float4 Tint;
            };

            [shader("vertex")]
            float4 MainVS(uint vertexId : SV_VertexID) : SV_Position { return float4(0, 0, 0, 1); }

            [shader("pixel")]
            float4 MainPS() : SV_Target
            {
                DrawRoot root = gpu_load_root<DrawRoot>();
                return root.Tint;
            }
            """;

        using RenderingTestHost host = Utility.CreateRenderingSystem();
        ShaderCompilerOptions options = new()
        {
            FileResolver = CreateResolverWithSource("VirtualTestModule.slang", source),
            SearchPaths = [],
        };
        using ShaderSystem shaders = host.RenderingSystem.CreateShaderSystem(options);

        Shader resolved = shaders.GetShader("VirtualTestModule");
        Assert.That(resolved.Name, Is.EqualTo("VirtualTestModule"));
        Assert.That(resolved.Kind, Is.EqualTo(ShaderKind.Graphics));
    }

    /// <summary>Verifies disk module discovery and relative imports and includes via <see cref="ShaderCompiler"/>.</summary>
    [Test]
    public void ShaderCompiler_DiskModulesAndIncludes_CompileCleanly()
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
            using ShaderCompiler compiler = new(ShaderTarget.Spirv, new ShaderCompilerOptions
            {
                SearchPaths = [directory],
                ValidateAbi = false,
            });
            CompiledShader compiled = compiler.CompileSource("test_disk_entry", path, source, ShaderPipelineKind.Graphics);
            Assert.That(compiled.Stages.Any(stage => stage.Stage == ShaderStageKind.Vertex), Is.True);
            Assert.That(compiled.Stages.Any(stage => stage.Stage == ShaderStageKind.Pixel), Is.True);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Verifies indexed resolver parity, O(1) existence probing without reading streams, and lazy version refresh.</summary>
    [Test]
    public void CreateIndexed_MatchesCreateAndRefreshesLazilyOnVersionChange()
    {
        int version = 0;
        List<string> assets = [];
        int openCalls = 0;
        Dictionary<string, string> contents = new(StringComparer.OrdinalIgnoreCase);

        (ShaderFileResolver resolver, ShaderPathExists pathExists) = ShaderModuleResolver.CreateIndexed(
            path =>
            {
                openCalls++;
                return contents.TryGetValue(path, out string? src)
                    ? new MemoryStream(Encoding.UTF8.GetBytes(src))
                    : null;
            },
            () => assets,
            () => version);

        // Must NOT enumerate assets eagerly before sources are registered.
        Assert.That(openCalls, Is.EqualTo(0));
        Assert.That(pathExists("AlcoRendering_Core.slang"), Is.False);
        Assert.That(openCalls, Is.EqualTo(0));

        // Populate assets and bump version (mirroring GameEngine adding file sources after CreateShaderCompilerOptions).
        const string expectedCore = "module AlcoRendering_Core;";
        const string expectedShadow = "module ShadowGpuCull;";
        const string expectedHeader = "#define HEADER 1";
        assets.AddRange([
            "Core.slang",
            "Shaders/Libs/AlcoRendering_Core.slang",
            "Shaders/Passes/Compute/AmbientShadowGpuCull.slang",
            "Shaders/Passes/Compute/Ambient_ShadowGpuCull.slang",
            "Shaders/Passes/Compute/ShadowGpuCull.slang",
            "Shaders/Libs/CommonHeader.slangh",
        ]);
        contents["Core.slang"] = "module Core;";
        contents["Shaders/Libs/AlcoRendering_Core.slang"] = expectedCore;
        contents["Shaders/Passes/Compute/AmbientShadowGpuCull.slang"] = "module AmbientShadowGpuCull;";
        contents["Shaders/Passes/Compute/Ambient_ShadowGpuCull.slang"] = "module Ambient_ShadowGpuCull;";
        contents["Shaders/Passes/Compute/ShadowGpuCull.slang"] = expectedShadow;
        contents["Shaders/Libs/CommonHeader.slangh"] = expectedHeader;
        version++;

        // PathExists checks must never open asset streams.
        Assert.Multiple(() =>
        {
            Assert.That(pathExists("AlcoRendering_Core.slang"), Is.True);
            Assert.That(pathExists("AlcoRendering-Core.slang"), Is.True);
            Assert.That(pathExists("AlcoRendering/Core.slang"), Is.True);
            Assert.That(pathExists("Assets/Shaders/Passes/AlcoRendering_Core.slang"), Is.True);
            Assert.That(pathExists("Shaders/Libs/CommonHeader.slangh"), Is.True);
            Assert.That(pathExists("MissingShader.slang"), Is.False);
            Assert.That(openCalls, Is.EqualTo(0), "PathExists must use the index without opening asset streams");
        });

        Assert.Multiple(() =>
        {
            Assert.That(resolver("AlcoRendering_Core.slang"), Is.EqualTo(expectedCore));
            Assert.That(resolver("AlcoRendering-Core.slang"), Is.EqualTo(expectedCore));
            Assert.That(resolver("AlcoRendering/Core.slang"), Is.EqualTo(expectedCore));
            Assert.That(resolver("Assets/Shaders/Passes/AlcoRendering_Core.slang"), Is.EqualTo(expectedCore));
            Assert.That(resolver("ShadowGpuCull.slang"), Is.EqualTo(expectedShadow));
            Assert.That(resolver("Shaders/Libs/CommonHeader.slangh"), Is.EqualTo(expectedHeader));
            Assert.That(resolver("MissingShader.slang"), Is.Null);
        });
    }

    private static ShaderFileResolver CreateResolverWithSource(string moduleFileName, string source)
        => ShaderModuleResolver.Create(
            path => string.Equals(path, moduleFileName, StringComparison.OrdinalIgnoreCase)
                ? new MemoryStream(Encoding.UTF8.GetBytes(source))
                : null,
            () => [moduleFileName]);

    private static ShaderFileResolver CreateResolver(params string[] assets)
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
