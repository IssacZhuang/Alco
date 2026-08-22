using NUnit.Framework;

namespace Alco.Rendering.Test;

public class SpirvCompatTest
{
    [TestCase("ShadersSlang/Pipelines/Rendering/PBR/DeferredLighting.slang", true)]
    [TestCase("ShadersSlang/Pipelines/Rendering/PBR/ScreenSpaceReflectionBlueNoise.slang", false)]
    [TestCase("ShadersSlang/Pipelines/Utils/BlitDepth.slang", false)]
    public void BackendSelectionIsNarrowAndDeterministic(string assetPath, bool expected)
    {
        Assert.That(SpirvCompat.RequiresGlslang(assetPath), Is.EqualTo(expected));
    }
}
