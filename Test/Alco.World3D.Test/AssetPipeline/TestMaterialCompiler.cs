using NUnit.Framework;
using Alco.Engine;
using Alco.Graphics;
using Alco.IO;
using Alco.Rendering;

namespace Alco.World3D.Test;

/// <summary>
/// Compilation of material assets into per-pass GPU materials: pass registration, the
/// (asset, pass) cache, derived pipeline state and the optional-pass guards, plus the
/// composition of custom surface shaders into the pass templates. Uses a NoGPU engine
/// with the module's real shaders, mirroring <see cref="ValidateShader"/>.
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
        using MaterialCompiler compiler = new(engine.RenderingSystem, assets);
        GBufferMaterialPass gbufferPass = new(gbuffer);
        compiler.RegisterPass(gbufferPass);

        MaterialAsset opaque = new() { Name = "opaque" };
        MaterialAsset doubled = new() { Name = "doubled", DoubleSided = true };

        GraphicsMaterial material = compiler.Get(opaque, gbufferPass);

        Assert.Multiple(() =>
        {
            // One material per (asset, pass), reused across requests.
            Assert.That(compiler.Get(opaque, gbufferPass), Is.SameAs(material));

            // The renderer applies reverse-Z depth for the G-buffer pass.
            Assert.That(material.DepthStencilState, Is.EqualTo(DepthStencilState.WriteReverseZ));

            // doubleSided derives the rasterizer cull mode.
            Assert.That(material.RasterizerState.CullMode, Is.EqualTo(CullMode.Back));
            Assert.That(compiler.Get(doubled, gbufferPass).RasterizerState.CullMode, Is.EqualTo(CullMode.None));

            // Passes that were never registered report unusable (e.g. the optional
            // pass of a feature that is disabled this run).
            Assert.That(compiler.TryGet(opaque, "shadow"), Is.Null);
            Assert.That(compiler.TryGet(opaque, "rsm"), Is.Null);

            // Registering a duplicate pass id is rejected.
            Assert.That(() => compiler.RegisterPass(new GBufferMaterialPass(gbuffer)), Throws.ArgumentException);
        });

        // Streaming textures (still null here) rebind without disturbing the compiled material.
        Assert.That(() => compiler.BindTextures(opaque, new Dictionary<string, Texture2D?>()), Throws.Nothing);

        // Invalidation drops the compiled material; the next request compiles a fresh one.
        compiler.Invalidate(opaque);
        GraphicsMaterial recompiled = compiler.Get(opaque, gbufferPass);
        Assert.That(recompiled, Is.Not.SameAs(material));
    }

    [Test]
    public void CustomSurfaceComposesIntoThePassTemplate()
    {
        using GameEngine engine = new(GameEngineSetting.CreateNoGPU());
        AssetSystem assets = engine.AssetSystem;
        World3DAssetPipeline.RegisterLoaders(assets, engine.RenderingSystem);

        Shader gbufferShader = assets.Load<Shader>(World3DAssetPaths.Shader_GBuffer);
        using GBufferRenderer gbuffer = new(engine.RenderingSystem, gbufferShader);
        using MaterialCompiler compiler = new(engine.RenderingSystem, assets);
        GBufferMaterialPass gbufferPass = new(gbuffer);
        compiler.RegisterPass(gbufferPass);

        // A procedural surface: composed into the G-buffer template, declaring no
        // texture slots at all (nothing to stream).
        MaterialAsset checker = new() { Name = "checker", SurfaceShader = "Shaders/Materials/Checker.hlsli" };
        GraphicsMaterial material = compiler.Get(checker, gbufferPass);

        Assert.Multiple(() =>
        {
            Assert.That(compiler.Get(checker, gbufferPass), Is.SameAs(material), "Composed materials cache per (asset, pass).");
            Assert.That(material.TryGetResourceId("_albedoTexture", out _), Is.False, "The checker surface declares no albedo slot.");
            Assert.That(material.TryGetResourceId(ShaderResourceId.Camera, out _), Is.True, "The pass template keeps its camera binding.");
            Assert.That(material.TryGetResourceId(ShaderResourceId.GlobalRenderData, out _), Is.True,
                "The surface's _globalRenderData declaration reaches the composed shader (time source).");
        });

        compiler.Invalidate(checker);
    }

    [Test]
    public void SurfaceParametersBindIntoTheComposedShader()
    {
        using GameEngine engine = new(GameEngineSetting.CreateNoGPU());
        AssetSystem assets = engine.AssetSystem;
        World3DAssetPipeline.RegisterLoaders(assets, engine.RenderingSystem);

        Shader gbufferShader = assets.Load<Shader>(World3DAssetPaths.Shader_GBuffer);
        using GBufferRenderer gbuffer = new(engine.RenderingSystem, gbufferShader);
        using MaterialCompiler compiler = new(engine.RenderingSystem, assets);
        GBufferMaterialPass gbufferPass = new(gbuffer);
        compiler.RegisterPass(gbufferPass);

        // The checker surface declares one _materialParams member; the asset's
        // parameter binds as the block's buffer resource.
        MaterialAsset scaled = new()
        {
            Name = "scaled",
            SurfaceShader = "Shaders/Materials/Checker.hlsli",
            Parameters = new Dictionary<string, float[]> { ["checkerScale"] = [4.0f] },
        };
        GraphicsMaterial material = compiler.Get(scaled, gbufferPass);
        Assert.That(material.TryGetResourceId("_materialParams", out _), Is.True,
            "The composed shader exposes the surface's parameter block.");

        // Unknown parameter names fail loudly (typo in the asset).
        MaterialAsset typo = new()
        {
            Name = "typo",
            SurfaceShader = "Shaders/Materials/Checker.hlsli",
            Parameters = new Dictionary<string, float[]> { ["scale"] = [4.0f] },
        };
        Assert.That(() => compiler.Get(typo, gbufferPass), Throws.TypeOf<InvalidDataException>());

        // The built-in surface declares no parameter block; parameters without a
        // custom surface are rejected instead of silently ignored.
        MaterialAsset builtinParams = new()
        {
            Name = "builtin",
            Parameters = new Dictionary<string, float[]> { ["checkerScale"] = [4.0f] },
        };
        Assert.That(() => compiler.Get(builtinParams, gbufferPass), Throws.TypeOf<InvalidDataException>());
    }
}
