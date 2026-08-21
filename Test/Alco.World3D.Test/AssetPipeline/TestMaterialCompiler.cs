using NUnit.Framework;
using Alco.Engine;
using Alco.Graphics;
using Alco.IO;
using Alco.Rendering;

namespace Alco.World3D.Test;

/// <summary>
/// Compilation of material assets into per-pass GPU materials: caching per asset, derived
/// pipeline state and the optional-pass guards. Uses a NoGPU engine with the module's real
/// GBuffer shader, mirroring <see cref="ValidateShader"/>.
/// </summary>
public class TestMaterialCompiler
{
    [Test]
    public void GBufferMaterialsCompileCacheAndInvalidate()
    {
        using GameEngine engine = new(GameEngineSetting.CreateNoGPU());
        AssetSystem assets = engine.AssetSystem;
        World3DAssetPipeline.RegisterLoaders(assets, engine.RenderingSystem);

        Shader gbufferShader = assets.Load<Shader>(World3DAssetPaths.Shader_GBuffer);
        using GBufferRenderer gbuffer = new(engine.RenderingSystem, gbufferShader);
        using MaterialCompiler compiler = new(engine.RenderingSystem, gbuffer);

        MaterialAsset opaque = new() { Name = "opaque" };
        MaterialAsset doubled = new() { Name = "doubled", DoubleSided = true };

        GraphicsMaterial material = compiler.GetGBuffer(opaque);

        Assert.Multiple(() =>
        {
            // One material per asset, reused across requests.
            Assert.That(compiler.GetGBuffer(opaque), Is.SameAs(material));

            // The renderer applies reverse-Z depth for the G-buffer pass.
            Assert.That(material.DepthStencilState, Is.EqualTo(DepthStencilState.WriteReverseZ));

            // doubleSided derives the rasterizer cull mode.
            Assert.That(material.RasterizerState.CullMode, Is.EqualTo(CullMode.Back));
            Assert.That(compiler.GetGBuffer(doubled).RasterizerState.CullMode, Is.EqualTo(CullMode.None));

            // Without a shadow renderer bound: shadow and RSM report unusable, glass throws.
            Assert.That(compiler.GetRsm(opaque), Is.Null);
            Assert.That(() => compiler.GetShadow(opaque), Throws.InvalidOperationException);
            Assert.That(() => compiler.GetForwardGlass(opaque), Throws.InvalidOperationException);
        });

        // Streaming textures (still null here) rebind without disturbing the compiled material.
        Assert.That(() => compiler.BindTextures(opaque, null, null, null, null), Throws.Nothing);

        // Invalidation drops the compiled material; the next request compiles a fresh one.
        compiler.Invalidate(opaque);
        GraphicsMaterial recompiled = compiler.GetGBuffer(opaque);
        Assert.That(recompiled, Is.Not.SameAs(material));
    }
}
