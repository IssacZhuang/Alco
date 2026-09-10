using System.Text;
using Alco.ShaderCompiler;
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
