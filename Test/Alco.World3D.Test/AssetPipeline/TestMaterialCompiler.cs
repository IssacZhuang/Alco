using System.IO;
#nullable enable

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
    /// <summary>
    /// Write a minimal procedural surface module into a temp directory mounted on the
    /// asset system: no textures, one <c>_materialParams</c> member (<c>scale</c>),
    /// engine time via <c>_globalRenderData</c>. The surface contract (ISurface)
    /// resolves from the engine's own asset source, so the temp source carries only
    /// the surface file. Returns the surface's asset path;
    /// <paramref name="directory"/> receives the temp directory to delete when the
    /// test ends.
    /// </summary>
    private static string WriteTestSurface(AssetSystem assets, out string directory)
    {
        directory = Path.Combine(Path.GetTempPath(), "alco_surface_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "TestSurface.slang"), """
            module test_surface;

            import surface;

            [[vk::binding(0, 2)]] cbuffer _materialParams
            {
                float4 scale; // x = cells per meter; 0 = the default 2
            };

            [[vk::binding(1, 2)]] cbuffer _globalRenderData
            {
                float4 time; // x = time, y = deltaTime, z = sinTime, w = cosTime
            };

            public struct Surface : ISurface
            {
                public void ModifyVertex(inout float3 worldPos, inout float3 normalWS, float2 uv)
                {
                }

                public float4 GetBaseColor(SurfaceInput input)
                {
                    float cellsPerMeter = scale.x > 0.0 ? scale.x : 2.0;
                    float3 cell = floor(input.worldPos * cellsPerMeter - time.x);
                    float checker = fmod(cell.x + cell.y + cell.z, 2.0);
                    float3 albedo = lerp(float3(0.90, 0.90, 0.92), float3(0.85, 0.12, 0.10), checker);
                    return float4(albedo * input.baseColorFactor.rgb, input.baseColorFactor.a);
                }

                public float3 GetNormalTS(SurfaceInput input)
                {
                    return float3(0.0, 0.0, 1.0);
                }

                public float3 GetMetallicRoughnessAO(SurfaceInput input)
                {
                    return float3(0.0, 0.5, 1.0);
                }

                public float3 GetEmissive(SurfaceInput input)
                {
                    return input.emissiveFactor.rgb;
                }
            }
            """);
        assets.AddFileSource(new DirectoryFileSource(directory));
        return "TestSurface.slang";
    }

    [Test]
    public void GBufferMaterialsCompileCacheAndInvalidate()
    {
        using GameEngine engine = new(GameEngineSetting.CreateNoGPU());
        AssetSystem assets = engine.AssetSystem;
        World3DAssetPipeline.RegisterLoaders(assets, engine.RenderingSystem);

        // Templates compose with the built-in surface through the compiler (the
        // direct template-as-asset load was retired: templates own no entry points).
        using MaterialCompiler compiler = new(engine.RenderingSystem, assets);
        using GBufferRenderer gbuffer = new(
            engine.RenderingSystem, compiler.GetTemplateShader(World3DAssetPaths.Shader_GBuffer));
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
        string surfacePath = WriteTestSurface(assets, out string directory);
        try
        {
            World3DAssetPipeline.RegisterLoaders(assets, engine.RenderingSystem);

            using MaterialCompiler compiler = new(engine.RenderingSystem, assets);
            using GBufferRenderer gbuffer = new(
                engine.RenderingSystem, compiler.GetTemplateShader(World3DAssetPaths.Shader_GBuffer));
            GBufferMaterialPass gbufferPass = new(gbuffer);
            compiler.RegisterPass(gbufferPass);

            // A procedural surface: composed into the G-buffer template, declaring no
            // texture slots at all (nothing to stream).
            MaterialAsset checker = new() { Name = "checker", SurfaceShader = surfacePath };
            GraphicsMaterial material = compiler.Get(checker, gbufferPass);

            Assert.Multiple(() =>
            {
                Assert.That(compiler.Get(checker, gbufferPass), Is.SameAs(material), "Composed materials cache per (asset, pass).");
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

            using MaterialCompiler compiler = new(engine.RenderingSystem, assets);
            using GBufferRenderer gbuffer = new(
                engine.RenderingSystem, compiler.GetTemplateShader(World3DAssetPaths.Shader_GBuffer));
            GBufferMaterialPass gbufferPass = new(gbuffer);
            compiler.RegisterPass(gbufferPass);

            // The test surface declares one _materialParams member; the asset's
            // parameter binds as the block's buffer resource.
            MaterialAsset scaled = new()
            {
                Name = "scaled",
                SurfaceShader = surfacePath,
                Parameters = new Dictionary<string, float[]> { ["scale"] = [4.0f] },
            };
            GraphicsMaterial material = compiler.Get(scaled, gbufferPass);
            Assert.That(material.TryGetResourceId("_materialParams", out _), Is.True,
                "The composed shader exposes the surface's parameter block.");

            // Unknown parameter names fail loudly (typo in the asset).
            MaterialAsset typo = new()
            {
                Name = "typo",
                SurfaceShader = surfacePath,
                Parameters = new Dictionary<string, float[]> { ["nonsense"] = [4.0f] },
            };
            Assert.That(() => compiler.Get(typo, gbufferPass), Throws.TypeOf<InvalidDataException>());

            // The built-in surface declares no parameter block; parameters without a
            // custom surface are rejected instead of silently ignored.
            MaterialAsset builtinParams = new()
            {
                Name = "builtin",
                Parameters = new Dictionary<string, float[]> { ["scale"] = [4.0f] },
            };
            Assert.That(() => compiler.Get(builtinParams, gbufferPass), Throws.TypeOf<InvalidDataException>());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
