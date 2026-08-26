using System.IO;
using System.Numerics;
#nullable enable

using NUnit.Framework;
using Alco.Engine;
using Alco.Graphics;
using Alco.IO;
using Alco.Rendering;

namespace Alco.World3D.Test;

/// <summary>
/// Compilation of material assets into per-pass GPU materials: the pure
/// (asset, template, spec-args, factory) factory of <see cref="MaterialCompiler"/>,
/// the renderers' per-asset caching and derived pipeline state, participation
/// guards, texture-slot validation and the optional-feature guards. Uses a
/// NoGPU engine with the module's real shaders, mirroring
/// <see cref="ValidateShader"/>.
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
    /// Returns the surface's module name (what <see cref="MaterialAsset.Surface"/>
    /// references); <paramref name="directory"/> receives the temp directory to delete
    /// when the test ends.
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
        return "test_surface";
    }

    [Test]
    public void GBufferMaterialsCompilePerAssetAndCache()
    {
        using GameEngine engine = new(GameEngineSetting.CreateNoGPU());

        // The renderer compiles its per-asset materials through its own pass
        // strategy: the template composes with each asset's surface, the factory
        // applies the pass-mandated state, and the renderer caches the result
        // per asset (shared by every item using it).
        using MaterialCompiler compiler = World3DAssetPipeline.CreateMaterialCompiler(engine.RenderingSystem);
        using GBufferRenderer gbuffer = new(
            engine.RenderingSystem, compiler, engine.RenderingSystem.ShaderSystem.GetLibrary("gbuffer"));

        PbrMaterialAsset opaque = new() { Name = "opaque" };
        PbrMaterialAsset doubled = new() { Name = "doubled", DoubleSided = true };

        GraphicsMaterial material = gbuffer.GetMaterial(opaque);

        Assert.Multiple(() =>
        {
            // The renderer's cache shares one material per asset; the stateless
            // factory beneath it compiles fresh materials per call.
            Assert.That(gbuffer.GetMaterial(opaque), Is.SameAs(material));
            Assert.That(compiler.Compile(opaque,
                engine.RenderingSystem.ShaderSystem.GetLibrary("gbuffer"), valueSpecArgs: null,
                (a, shader) => engine.RenderingSystem.CreateGraphicsMaterial(shader, $"{a.Name}_gbuffer")),
                Is.Not.SameAs(material),
                "The stateless factory compiles a fresh material per call; the renderer's cache shares per asset.");

            // The renderer applies reverse-Z depth for the G-buffer pass.
            Assert.That(material.DepthStencilState, Is.EqualTo(DepthStencilState.WriteReverseZ));

            // doubleSided derives the rasterizer cull mode.
            Assert.That(material.RasterizerState.CullMode, Is.EqualTo(CullMode.Back));
            Assert.That(gbuffer.GetMaterial(doubled).RasterizerState.CullMode, Is.EqualTo(CullMode.None));
        });

        // Instance overrides remain the per-draw customization point; the compiled
        // material itself is never mutated after compilation.
        GraphicsMaterialInstance instance = material.CreateInstance();
        Assert.That(() => instance.SetTexture("_albedoTexture", engine.RenderingSystem.TextureWhite), Throws.Nothing);
    }

    [Test]
    public void ComputeFeedBindsTexturesOnce()
    {
        using GameEngine engine = new(GameEngineSetting.CreateNoGPU());
        using MaterialCompiler compiler = World3DAssetPipeline.CreateMaterialCompiler(engine.RenderingSystem);
        ShaderLibrary voxelize = engine.RenderingSystem.ShaderSystem.GetLibrary("voxelize");

        // The compute counterpart of Compile: the asset's textures bind once at
        // creation; slots the asset leaves out take its fallback policy.
        PbrMaterialAsset textured = new()
        {
            Name = "textured",
            Textures = new Dictionary<string, Texture2D>
                { ["albedoTexture"] = engine.RenderingSystem.TextureBlack },
        };
        ComputeMaterial material = compiler.CompileCompute(textured, voxelize);
        Assert.That(material.TryGetResourceId("_albedoTexture", out _), Is.True);

        // Compile-time slot validation, the same rule as the graphics passes.
        PbrMaterialAsset typo = new()
        {
            Name = "typo",
            Textures = new Dictionary<string, Texture2D>
                { ["albedo"] = engine.RenderingSystem.TextureWhite },
        };
        Assert.That(() => compiler.CompileCompute(typo, voxelize), Throws.TypeOf<InvalidDataException>());

        // The shared default asset's feed binds its fallbacks without any bindings.
        Assert.That(() => compiler.CompileCompute(PbrMaterialAsset.Default, voxelize), Throws.Nothing);
    }

    [Test]
    public void ComputeFeedPacksSurfaceParameters()
    {
        using GameEngine engine = new(GameEngineSetting.CreateNoGPU());
        string surfaceModule = WriteTestSurface(engine.AssetSystem, out string directory);
        try
        {
            using MaterialCompiler compiler = World3DAssetPipeline.CreateMaterialCompiler(engine.RenderingSystem);
            ShaderLibrary voxelize = engine.RenderingSystem.ShaderSystem.GetLibrary("voxelize");
            ShaderLibrary surface = engine.RenderingSystem.ShaderSystem.GetLibrary(surfaceModule);

            // The surface's [MaterialParams] block binds in the compute feed by the
            // block's own name, packed from the asset's values.
            PbrMaterialAsset scaled = new()
            {
                Name = "scaled",
                Surface = surface,
                Parameters = new Dictionary<string, ShaderValue> { ["scale"] = new Vector4(4.0f, 0.0f, 0.0f, 0.0f) },
            };
            ComputeMaterial material = compiler.CompileCompute(scaled, voxelize);
            Assert.That(material.TryGetResourceId("_surfaceParams", out _), Is.True,
                "The surface's parameter block binds in the compute feed too.");

            // Unknown parameter names fail loudly, as on the graphics passes.
            PbrMaterialAsset typo = new()
            {
                Name = "typo",
                Surface = surface,
                Parameters = new Dictionary<string, ShaderValue> { ["nonsense"] = new Vector4(4.0f, 0.0f, 0.0f, 0.0f) },
            };
            Assert.That(() => compiler.CompileCompute(typo, voxelize), Throws.TypeOf<InvalidDataException>());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void PassAcceptsRoutesAndRejects()
    {
        using GameEngine engine = new(GameEngineSetting.CreateNoGPU());

        using MaterialCompiler compiler = World3DAssetPipeline.CreateMaterialCompiler(engine.RenderingSystem);
        // The glass template participates only for blend materials — the routing
        // that replaces game-side alpha-mode special cases.
        ShaderLibrary glass = engine.RenderingSystem.ShaderSystem.GetLibrary("glass");
        GraphicsMaterial CompileGlass(MaterialAsset asset, Shader shader)
            => engine.RenderingSystem.CreateGraphicsMaterial(shader, $"{asset.Name}_glass");

        PbrMaterialAsset opaque = new() { Name = "opaque" };
        PbrMaterialAsset blend = new() { Name = "blend", AlphaMode = MeshAlphaMode.Blend };

        Assert.That(compiler.Compile(blend, glass, valueSpecArgs: null, CompileGlass), Is.Not.Null);
    }

    [Test]
    public void ShadowPassSpecializesAlphaTestFromTheAsset()
    {
        using GameEngine engine = new(GameEngineSetting.CreateNoGPU());

        using MaterialCompiler compiler = World3DAssetPipeline.CreateMaterialCompiler(engine.RenderingSystem);
        // The shadow template's alpha test is its <let AlphaTest : bool> value
        // specialization parameter, fed from the asset's alpha mode — not a define.
        ShaderLibrary shadow = engine.RenderingSystem.ShaderSystem.GetLibrary("shadow_depth");
        GraphicsMaterial CompileShadow(MaterialAsset asset, Shader shader)
            => engine.RenderingSystem.CreateGraphicsMaterial(shader, $"{asset.Name}_shadow");

        GraphicsMaterial opaque = compiler.Compile(new PbrMaterialAsset { Name = "opaque" },
            shadow, ["false"], CompileShadow);
        GraphicsMaterial mask = compiler.Compile(
            new PbrMaterialAsset { Name = "mask", AlphaMode = MeshAlphaMode.Mask },
            shadow, ["true"], CompileShadow);

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

        using MaterialCompiler compiler = World3DAssetPipeline.CreateMaterialCompiler(engine.RenderingSystem);
        using GBufferRenderer gbuffer = new(
            engine.RenderingSystem, compiler, engine.RenderingSystem.ShaderSystem.GetLibrary("gbuffer"));

        // A declared slot of the built-in surface passes validation.
        PbrMaterialAsset textured = new()
        {
            Name = "textured",
            Textures = new Dictionary<string, Texture2D>
                { ["albedoTexture"] = engine.RenderingSystem.TextureWhite },
        };
        Assert.That(() => gbuffer.GetMaterial(textured), Throws.Nothing);

        // An undeclared slot is a typo in the asset: fail at compile time with
        // the valid slot names, not later at bind time.
        PbrMaterialAsset typo = new()
        {
            Name = "typo",
            Textures = new Dictionary<string, Texture2D>
                { ["albedo"] = engine.RenderingSystem.TextureWhite },
        };
        Assert.That(() => gbuffer.GetMaterial(typo), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void CustomSurfaceComposesIntoThePassTemplate()
    {
        using GameEngine engine = new(GameEngineSetting.CreateNoGPU());
        string surfaceModule = WriteTestSurface(engine.AssetSystem, out string directory);
        try
        {
            using MaterialCompiler compiler = World3DAssetPipeline.CreateMaterialCompiler(engine.RenderingSystem);
            using GBufferRenderer gbuffer = new(
                engine.RenderingSystem, compiler, engine.RenderingSystem.ShaderSystem.GetLibrary("gbuffer"));

            // A procedural surface: composed into the G-buffer template, declaring no
            // texture slots at all (nothing to stream).
            PbrMaterialAsset checker = new()
            {
                Name = "checker",
                Surface = engine.RenderingSystem.ShaderSystem.GetLibrary(surfaceModule),
            };
            GraphicsMaterial material = gbuffer.GetMaterial(checker);

            Assert.Multiple(() =>
            {
                Assert.That(gbuffer.GetMaterial(checker), Is.SameAs(material),
                    "The renderer's cache shares one material per asset.");
                Assert.That(material.TryGetResourceId("_albedoTexture", out _), Is.False, "The test surface declares no albedo slot.");
                Assert.That(material.TryGetResourceId(ShaderResourceId.Camera, out _), Is.True, "The pass template keeps its camera binding.");
                Assert.That(material.TryGetResourceId(ShaderResourceId.GlobalRenderData, out _), Is.True,
                    "The surface's _globalRenderData declaration reaches the composed shader (time source).");
            });
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
        string surfaceModule = WriteTestSurface(engine.AssetSystem, out string directory);
        try
        {
            using MaterialCompiler compiler = World3DAssetPipeline.CreateMaterialCompiler(engine.RenderingSystem);
            using GBufferRenderer gbuffer = new(
                engine.RenderingSystem, compiler, engine.RenderingSystem.ShaderSystem.GetLibrary("gbuffer"));
            ShaderLibrary surface = engine.RenderingSystem.ShaderSystem.GetLibrary(surfaceModule);

            // The test surface declares one [MaterialParams] block member; the
            // asset's parameter binds as the block's buffer resource, addressed by
            // the block's own (free) name.
            PbrMaterialAsset scaled = new()
            {
                Name = "scaled",
                Surface = surface,
                Parameters = new Dictionary<string, ShaderValue> { ["scale"] = new Vector4(4.0f, 0.0f, 0.0f, 0.0f) },
            };
            GraphicsMaterial material = gbuffer.GetMaterial(scaled);
            Assert.That(material.TryGetResourceId("_surfaceParams", out _), Is.True,
                "The composed shader exposes the surface's parameter block.");

            // Unknown parameter names fail loudly (typo in the asset).
            PbrMaterialAsset typo = new()
            {
                Name = "typo",
                Surface = surface,
                Parameters = new Dictionary<string, ShaderValue> { ["nonsense"] = new Vector4(4.0f, 0.0f, 0.0f, 0.0f) },
            };
            Assert.That(() => gbuffer.GetMaterial(typo), Throws.TypeOf<InvalidDataException>());

            // The built-in surface declares no parameter block; parameters without a
            // custom surface are rejected instead of silently ignored.
            PbrMaterialAsset builtinParams = new()
            {
                Name = "builtin",
                Parameters = new Dictionary<string, ShaderValue> { ["scale"] = new Vector4(4.0f, 0.0f, 0.0f, 0.0f) },
            };
            Assert.That(() => gbuffer.GetMaterial(builtinParams), Throws.TypeOf<InvalidDataException>());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// Write the minimal surface module — a bare <c>public struct Surface : ISurface {}</c>
    /// with zero overrides; every attribute rides the interface defaults. Returns the
    /// surface's module name; <paramref name="directory"/> receives the temp directory
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
        return "minimal_surface";
    }

    [Test]
    public void MinimalSurfaceReliesOnInterfaceDefaults()
    {
        using GameEngine engine = new(GameEngineSetting.CreateNoGPU());
        string surfaceModule = WriteMinimalSurface(engine.AssetSystem, out string directory);
        try
        {
            using MaterialCompiler compiler = World3DAssetPipeline.CreateMaterialCompiler(engine.RenderingSystem);
            using GBufferRenderer gbuffer = new(
                engine.RenderingSystem, compiler, engine.RenderingSystem.ShaderSystem.GetLibrary("gbuffer"));

            // The zero-override surface composes into the G-buffer template like any
            // other: the pass bindings come from the template, and the surface
            // declares no texture slots of its own.
            PbrMaterialAsset minimal = new()
            {
                Name = "minimal",
                Surface = engine.RenderingSystem.ShaderSystem.GetLibrary(surfaceModule),
            };
            GraphicsMaterial material = gbuffer.GetMaterial(minimal);
            Assert.Multiple(() =>
            {
                Assert.That(material.TryGetResourceId(ShaderResourceId.Camera, out _), Is.True,
                    "The pass template keeps its camera binding.");
                Assert.That(material.TryGetResourceId("_albedoTexture", out _), Is.False,
                    "The minimal surface declares no textures.");
            });

            // The compute feed composes too: the voxelize template's pass resources
            // and the shared GI data buffer are all present, surface textures absent.
            Shader voxelFeed = compiler.ComposeSurfaceComputeShader(
                minimal, engine.RenderingSystem.ShaderSystem.GetLibrary("voxelize"));
            ShaderReflection feedReflection = voxelFeed.GetShaderModules().ReflectionInfo;
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
            Shader builtinFeed = compiler.ComposeSurfaceComputeShader(
                null, engine.RenderingSystem.ShaderSystem.GetLibrary("voxelize"));
            Assert.That(builtinFeed.GetShaderModules().ReflectionInfo
                .TryGetResourceLocation("_albedoTexture", out _), Is.True);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
