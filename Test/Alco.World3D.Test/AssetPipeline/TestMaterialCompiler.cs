using System.IO;
#nullable enable

using NUnit.Framework;
using Alco.Engine;
using Alco.Graphics;
using Alco.IO;
using Alco.Rendering;

namespace Alco.World3D.Test;

/// <summary>
/// Compilation of material assets into per-pass GPU materials: pass registration
/// (<see cref="IMaterialPass"/>), the (asset, pass) cache, derived pipeline state,
/// <see cref="IMaterialPass.Accepts"/> routing, texture-slot validation and the
/// optional-pass guards. Uses a NoGPU engine with the module's real shaders,
/// mirroring <see cref="ValidateShader"/>.
/// </summary>
public class TestMaterialCompiler
{
    /// <summary>
    /// Write a minimal procedural surface module into a temp directory mounted on the
    /// asset system: no textures, one <c>[MaterialParams]</c>-marked block member
    /// (<c>scale</c>, the block name itself is free), engine time via
    /// <c>_globalRenderData</c>. The surface contract (ISurface)
    /// resolves from the engine's own asset source, so the temp source carries only
    /// the surface file; every uncustomized attribute rides the interface defaults.
    /// Returns the surface's asset path;
    /// <paramref name="directory"/> receives the temp directory to delete when the
    /// test ends.
    /// </summary>
    private static string WriteTestSurface(AssetSystem assets, out string directory)
    {
        directory = Path.Combine(Path.GetTempPath(), "alco_surface_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "test-surface.slang"), """
            module test_surface;

            import alco_world3d_surface;
            import alco_rendering_core;

            cbuffer _globalRenderData : register(b0, space2)
            {
                float4 time; // x = time, y = deltaTime, z = sinTime, w = cosTime
            };

            [MaterialParams]
            cbuffer _surfaceParams : register(b1, space2)
            {
                float4 scale; // x = cells per meter; 0 = the default 2
            };

            public struct Surface : ISurface
            {
                public override float4 GetBaseColor(SurfaceInput input)
                {
                    float cellsPerMeter = scale.x > 0.0 ? scale.x : 2.0;
                    float3 cell = floor(input.worldPos * cellsPerMeter - time.x);
                    float checker = fmod(cell.x + cell.y + cell.z, 2.0);
                    float3 albedo = lerp(float3(0.90, 0.90, 0.92), float3(0.85, 0.12, 0.10), checker);
                    return float4(albedo * input.baseColorFactor.rgb, input.baseColorFactor.a);
                }

                public override float3 GetMetallicRoughnessAO(SurfaceInput input)
                {
                    return float3(0.0, 0.5, 1.0);
                }
            }
            """);
        assets.AddFileSource(new DirectoryFileSource(directory));
        return "test-surface.slang";
    }

    [Test]
    public void GBufferMaterialsCompileCacheAndInvalidate()
    {
        using GameEngine engine = new(GameEngineSetting.CreateNoGPU());
        AssetSystem assets = engine.AssetSystem;
        World3DAssetPipeline.RegisterLoaders(assets, engine.RenderingSystem);

        // The renderer's constructor registers itself as the "gbuffer" pass: the
        // template composes with each asset's surface, the renderer factory applies
        // the pass-mandated state.
        using MaterialCompiler compiler = World3DAssetPipeline.CreateMaterialCompiler(engine.RenderingSystem);
        using GBufferRenderer gbuffer = new(engine.RenderingSystem, compiler);

        PbrMaterialAsset opaque = new() { Name = "opaque" };
        PbrMaterialAsset doubled = new() { Name = "doubled", DoubleSided = true };

        GraphicsMaterial material = compiler.Get(opaque, "gbuffer");

        Assert.Multiple(() =>
        {
            // One material per (asset, pass), reused across requests.
            Assert.That(compiler.Get(opaque, "gbuffer"), Is.SameAs(material));

            // The renderer applies reverse-Z depth for the G-buffer pass.
            Assert.That(material.DepthStencilState, Is.EqualTo(DepthStencilState.WriteReverseZ));

            // doubleSided derives the rasterizer cull mode.
            Assert.That(material.RasterizerState.CullMode, Is.EqualTo(CullMode.Back));
            Assert.That(compiler.Get(doubled, "gbuffer").RasterizerState.CullMode, Is.EqualTo(CullMode.None));

            // Passes that were never registered report unusable (e.g. the optional
            // pass of a feature that is disabled this run).
            Assert.That(compiler.TryGet(opaque, "shadow"), Is.Null);
            Assert.That(compiler.TryGet(opaque, "rsm"), Is.Null);

            // Registering a duplicate pass id is rejected.
            Assert.That(() => compiler.RegisterPass(
                new StubMaterialPass("gbuffer", "gbuffer", engine.RenderingSystem)), Throws.ArgumentException);
        });

        // Streaming textures (still null here) rebind without disturbing the compiled material.
        Assert.That(() => compiler.BindTextures(opaque, new Dictionary<string, Texture2D?>()), Throws.Nothing);

        // Invalidation drops the compiled material; the next request compiles a fresh one.
        compiler.Invalidate(opaque);
        GraphicsMaterial recompiled = compiler.Get(opaque, "gbuffer");
        Assert.That(recompiled, Is.Not.SameAs(material));
    }

    [Test]
    public void PassAcceptsRoutesAndRejects()
    {
        using GameEngine engine = new(GameEngineSetting.CreateNoGPU());
        AssetSystem assets = engine.AssetSystem;
        World3DAssetPipeline.RegisterLoaders(assets, engine.RenderingSystem);

        using MaterialCompiler compiler = World3DAssetPipeline.CreateMaterialCompiler(engine.RenderingSystem);
        // The glass pass participates only for blend materials — the routing that
        // replaces game-side alpha-mode special cases.
        compiler.RegisterPass(new StubGlassPass(engine.RenderingSystem));

        PbrMaterialAsset opaque = new() { Name = "opaque" };
        PbrMaterialAsset blend = new() { Name = "blend", AlphaMode = MeshAlphaMode.Blend };

        Assert.Multiple(() =>
        {
            Assert.That(compiler.Accepts(opaque, "glass"), Is.False);
            Assert.That(compiler.Accepts(blend, "glass"), Is.True);
            Assert.That(compiler.TryGet(opaque, "glass"), Is.Null, "A rejecting pass yields no material.");
            Assert.That(compiler.TryGet(blend, "glass"), Is.Not.Null);
            Assert.That(() => compiler.Get(opaque, "glass"), Throws.TypeOf<InvalidDataException>(),
                "Getting a rejecting pass directly is a usage error.");
        });
    }

    [Test]
    public void ShadowPassSpecializesAlphaTestFromTheAsset()
    {
        using GameEngine engine = new(GameEngineSetting.CreateNoGPU());
        AssetSystem assets = engine.AssetSystem;
        World3DAssetPipeline.RegisterLoaders(assets, engine.RenderingSystem);

        using MaterialCompiler compiler = World3DAssetPipeline.CreateMaterialCompiler(engine.RenderingSystem);
        // The shadow pass feeds its template's <let AlphaTest : bool> parameter
        // from the asset's alpha mode — value specialization, not a define.
        compiler.RegisterPass(new StubShadowPass(engine.RenderingSystem));

        GraphicsMaterial opaque = compiler.Get(new PbrMaterialAsset { Name = "opaque" }, "shadow");
        GraphicsMaterial mask = compiler.Get(
            new PbrMaterialAsset { Name = "mask", AlphaMode = MeshAlphaMode.Mask }, "shadow");

        Assert.Multiple(() =>
        {
            Assert.That(opaque.Shader, Is.Not.SameAs(mask.Shader),
                "Distinct specializations compile distinct shaders.");
            Assert.That(opaque.Shader.Name, Does.Contain("[false]"));
            Assert.That(mask.Shader.Name, Does.Contain("[true]"));
        });
    }

    [Test]
    public void TextureSlotsValidateAgainstTheSurface()
    {
        using GameEngine engine = new(GameEngineSetting.CreateNoGPU());
        AssetSystem assets = engine.AssetSystem;
        World3DAssetPipeline.RegisterLoaders(assets, engine.RenderingSystem);

        using MaterialCompiler compiler = World3DAssetPipeline.CreateMaterialCompiler(engine.RenderingSystem);
        using GBufferRenderer gbuffer = new(engine.RenderingSystem, compiler);

        // A declared slot of the built-in surface passes validation (the texture
        // path itself is never loaded by the compiler).
        PbrMaterialAsset textured = new()
        {
            Name = "textured",
            Textures = new Dictionary<string, string> { ["albedoTexture"] = "wall.png" },
        };
        Assert.That(() => compiler.Get(textured, "gbuffer"), Throws.Nothing);

        // An undeclared slot is a typo in the asset: fail at compile time with
        // the valid slot names, not later at BindTextures.
        PbrMaterialAsset typo = new()
        {
            Name = "typo",
            Textures = new Dictionary<string, string> { ["albedo"] = "wall.png" },
        };
        Assert.That(() => compiler.Get(typo, "gbuffer"), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void CustomSurfaceComposesIntoThePassTemplate()
    {
        using GameEngine engine = new(GameEngineSetting.CreateNoGPU());
        AssetSystem assets = engine.AssetSystem;
        string surfacePath = WriteTestSurface(assets, out string directory);
        try
        {
            World3DAssetPipeline.RegisterLoaders(assets, engine.RenderingSystem);

            using MaterialCompiler compiler = World3DAssetPipeline.CreateMaterialCompiler(engine.RenderingSystem);
            using GBufferRenderer gbuffer = new(engine.RenderingSystem, compiler);

            // A procedural surface: composed into the G-buffer template, declaring no
            // texture slots at all (nothing to stream).
            PbrMaterialAsset checker = new() { Name = "checker", SurfaceShader = surfacePath };
            GraphicsMaterial material = compiler.Get(checker, "gbuffer");

            Assert.Multiple(() =>
            {
                Assert.That(compiler.Get(checker, "gbuffer"), Is.SameAs(material), "Composed materials cache per (asset, pass).");
                Assert.That(material.TryGetResourceId("_albedoTexture", out _), Is.False, "The test surface declares no albedo slot.");
                Assert.That(material.TryGetResourceId(ShaderResourceId.Camera, out _), Is.True, "The pass template keeps its camera binding.");
                Assert.That(material.TryGetResourceId(ShaderResourceId.GlobalRenderData, out _), Is.True,
                    "The surface's _globalRenderData declaration reaches the composed shader (time source).");
            });

            compiler.Invalidate(checker);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void SurfaceParametersBindIntoTheComposedShader()
    {
        using GameEngine engine = new(GameEngineSetting.CreateNoGPU());
        AssetSystem assets = engine.AssetSystem;
        string surfacePath = WriteTestSurface(assets, out string directory);
        try
        {
            World3DAssetPipeline.RegisterLoaders(assets, engine.RenderingSystem);

            using MaterialCompiler compiler = World3DAssetPipeline.CreateMaterialCompiler(engine.RenderingSystem);
            using GBufferRenderer gbuffer = new(engine.RenderingSystem, compiler);

            // The test surface declares one [MaterialParams] block member; the
            // asset's parameter binds as the block's buffer resource, addressed by
            // the block's own (free) name.
            PbrMaterialAsset scaled = new()
            {
                Name = "scaled",
                SurfaceShader = surfacePath,
                Parameters = new Dictionary<string, float[]> { ["scale"] = [4.0f] },
            };
            GraphicsMaterial material = compiler.Get(scaled, "gbuffer");
            Assert.That(material.TryGetResourceId("_surfaceParams", out _), Is.True,
                "The composed shader exposes the surface's parameter block.");

            // Unknown parameter names fail loudly (typo in the asset).
            PbrMaterialAsset typo = new()
            {
                Name = "typo",
                SurfaceShader = surfacePath,
                Parameters = new Dictionary<string, float[]> { ["nonsense"] = [4.0f] },
            };
            Assert.That(() => compiler.Get(typo, "gbuffer"), Throws.TypeOf<InvalidDataException>());

            // The built-in surface declares no parameter block; parameters without a
            // custom surface are rejected instead of silently ignored.
            PbrMaterialAsset builtinParams = new()
            {
                Name = "builtin",
                Parameters = new Dictionary<string, float[]> { ["scale"] = [4.0f] },
            };
            Assert.That(() => compiler.Get(builtinParams, "gbuffer"), Throws.TypeOf<InvalidDataException>());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// Write the minimal surface module — a bare <c>public struct Surface : ISurface {}</c>
    /// with zero overrides; every attribute rides the interface defaults. Returns the
    /// surface's asset path; <paramref name="directory"/> receives the temp directory
    /// to delete when the test ends.
    /// </summary>
    private static string WriteMinimalSurface(AssetSystem assets, out string directory)
    {
        directory = Path.Combine(Path.GetTempPath(), "alco_surface_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "minimal-surface.slang"), """
            module minimal_surface;

            import alco_world3d_surface;

            public struct Surface : ISurface {}
            """);
        assets.AddFileSource(new DirectoryFileSource(directory));
        return "minimal-surface.slang";
    }

    [Test]
    public void MinimalSurfaceReliesOnInterfaceDefaults()
    {
        using GameEngine engine = new(GameEngineSetting.CreateNoGPU());
        AssetSystem assets = engine.AssetSystem;
        string surfacePath = WriteMinimalSurface(assets, out string directory);
        try
        {
            World3DAssetPipeline.RegisterLoaders(assets, engine.RenderingSystem);

            using MaterialCompiler compiler = World3DAssetPipeline.CreateMaterialCompiler(engine.RenderingSystem);
            using GBufferRenderer gbuffer = new(engine.RenderingSystem, compiler);

            // The zero-override surface composes into the G-buffer template like any
            // other: the pass bindings come from the template, and the surface
            // declares no texture slots of its own.
            PbrMaterialAsset minimal = new() { Name = "minimal", SurfaceShader = surfacePath };
            GraphicsMaterial material = compiler.Get(minimal, "gbuffer");
            Assert.Multiple(() =>
            {
                Assert.That(material.TryGetResourceId(ShaderResourceId.Camera, out _), Is.True,
                    "The pass template keeps its camera binding.");
                Assert.That(material.TryGetResourceId("_albedoTexture", out _), Is.False,
                    "The minimal surface declares no textures.");
            });

            // The compute feed composes too: the voxelize template's pass resources
            // and the shared GI data buffer are all present, surface textures absent.
            Shader voxelFeed = compiler.ComposeSurfaceComputeShader(minimal, "voxelize");
            ShaderReflectionInfo feedReflection = voxelFeed.GetShaderModules().ReflectionInfo;
            Assert.Multiple(() =>
            {
                foreach (string name in new[] { "_data", "_vertices", "_indices", "_attrOut", "_pageTable" })
                {
                    Assert.That(feedReflection.TryGetResourceLocation(name, out _), Is.True,
                        $"The voxelize feed is missing {name}.");
                }
                Assert.That(feedReflection.TryGetResourceLocation("_albedoTexture", out _), Is.False);
            });

            // The built-in surface's explicit bindings stay visible across the fold
            // (specialization folds code, not bindings).
            Shader builtinFeed = compiler.ComposeSurfaceComputeShader(null, "voxelize");
            Assert.That(builtinFeed.GetShaderModules().ReflectionInfo
                .TryGetResourceLocation("_albedoTexture", out _), Is.True);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
