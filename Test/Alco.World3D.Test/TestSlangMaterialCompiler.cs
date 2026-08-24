#nullable enable

using System.IO;
using NUnit.Framework;
using Alco.Engine;
using Alco.Graphics;
using Alco.IO;
using Alco.Rendering;
using Alco.ShaderCompiler;

namespace Alco.World3D.Test;

/// <summary>
/// The Slang material path of <see cref="MaterialCompiler"/>: pass templates compose
/// with surface modules through slang's component system (composite + link-time
/// specialization of the template's generic entry points — no generated wrappers),
/// Slang's own reflection translated into the engine's shader reflection, and the
/// reflection-driven mapping of <c>[MaterialParams]</c>-marked parameter blocks.
/// Uses a NoGPU engine with the module's real Slang sources, mirroring
/// <see cref="AssetPipeline.TestMaterialCompiler"/>; the retired DXC toolchain and
/// the retired wrapper generator are not involved anywhere.
/// </summary>
public class TestSlangMaterialCompiler
{
    private const string ParameterizedSurfacePath = "Shaders/Materials/parameterized-surface.slang";

    [Test]
    public void TrivialShaderCompilesFromRegisteredSource()
    {
        // Bisects the bridge: a self-contained module touching no imports, so any
        // failure here is the session/module/entry-point plumbing, not assets.
        using GameEngine engine = new(GameEngineSetting.CreateNoGPU());
        string source = """
            module trivial_probe;

            [shader("vertex")]
            float4 MainVS(float3 position : POSITION) : SV_POSITION
            {
                return float4(position, 1.0);
            }

            [shader("pixel")]
            float4 MainPS() : SV_TARGET
            {
                return float4(1.0, 0.0, 1.0, 1.0);
            }
            """;
        Shader shader = engine.RenderingSystem.ShaderSystem.GetShaderFromModule(
            "trivial_probe", "trivial_probe.slang", source);
        ShaderModulesInfo modules = shader.GetShaderModules();

        Assert.Multiple(() =>
        {
            Assert.That(BitConverter.ToUInt32(modules.VertexShader.GetValueOrDefault().Source.ToArray(), 0), Is.EqualTo(0x07230203u));
            Assert.That(BitConverter.ToUInt32(modules.FragmentShader.GetValueOrDefault().Source.ToArray(), 0), Is.EqualTo(0x07230203u));
            Assert.That(modules.ReflectionInfo.BindGroups.Count, Is.EqualTo(0));
        });
    }

    [Test]
    public void BuiltInPbrSurfaceComposesIntoTheGBufferTemplate()
    {
        using GameEngine engine = new(GameEngineSetting.CreateNoGPU());
        using MaterialCompiler compiler = World3DAssetPipeline.CreateMaterialCompiler(engine.RenderingSystem);

        Shader shader = compiler.ComposeSurfaceShader(null, "gbuffer");
        ShaderModulesInfo modules = shader.GetShaderModules();
        ShaderReflectionInfo info = modules.ReflectionInfo;

        Assert.Multiple(() =>
        {
            Assert.That(BitConverter.ToUInt32(modules.VertexShader.GetValueOrDefault().Source.ToArray(), 0), Is.EqualTo(0x07230203u));
            Assert.That(BitConverter.ToUInt32(modules.FragmentShader.GetValueOrDefault().Source.ToArray(), 0), Is.EqualTo(0x07230203u));

            // Set-scoped blocks (plan D2): camera set 0, instances set 1,
            // surface resources set 2; bindings inside a set are compiler-assigned.
            Assert.That(info.BindGroups.Count, Is.EqualTo(3));
            foreach (string name in new[]
                     {
                         "_camera", "_instances", "_albedoTexture", "_normalTexture",
                         "_metallicRoughnessTexture", "_emissiveTexture",
                     })
            {
                Assert.That(info.TryGetResourceLocation(name, out _), Is.True, $"Missing {name}");
            }
        });
    }

    [Test]
    public void ComposedReflectionReportsTheEngineLayout()
    {
        using GameEngine engine = new(GameEngineSetting.CreateNoGPU());
        using MaterialCompiler compiler = World3DAssetPipeline.CreateMaterialCompiler(engine.RenderingSystem);

        // The real composition the material compiler produces for the G-buffer
        // template with the test surface (mixed-type parameter block).
        MaterialAsset asset = new() { Name = "parameterized", SurfaceShader = ParameterizedSurfacePath };
        Shader shader = compiler.ComposeSurfaceShader(asset, "gbuffer");
        ShaderReflectionInfo info = shader.GetShaderModules().ReflectionInfo;

        Assert.Multiple(() =>
        {
            AssertResource(info, "_camera", 0, 0, BindingType.UniformBuffer);
            AssertResource(info, "_instances", 1, 0, BindingType.StorageBuffer);
            AssertResource(info, "_globalRenderData", 2, 0, BindingType.UniformBuffer);
            AssertResource(info, "PulseParams", 2, 1, BindingType.UniformBuffer);
            AssertResource(info, "_albedoTexture", 2, 2, BindingType.Texture);
            AssertResource(info, "_normalTexture", 2, 4, BindingType.Texture);
            AssertResource(info, "_metallicRoughnessTexture", 2, 6, BindingType.Texture);

            // Samplers are companion entries bound with their owning texture
            // (ShaderParameterSet's OwnerSampler plan), not name-addressable
            // resources — assert them through the bind group layouts.
            AssertLayoutEntry(info, "_albedoTextureSampler", 2, 3, BindingType.Sampler);

            // The vertex layout matches Alco.Rendering.VertexPBR exactly.
            Assert.That(info.VertexLayouts.Count, Is.EqualTo(1));
            VertexInputLayout layout = info.VertexLayouts[0];
            Assert.That(layout.Stride, Is.EqualTo(48u));
            Assert.That(layout.Elements.Select(element => element.Name),
                Is.EqualTo(new[] { "position", "normal", "uv", "tangent" }));
            Assert.That(layout.Elements.Select(element => element.Location), Is.EqualTo(new uint[] { 0, 1, 2, 3 }));
            Assert.That(layout.Elements.Select(element => element.Offset), Is.EqualTo(new uint[] { 0, 12, 24, 32 }));
            Assert.That(layout.Elements.Select(element => element.Format),
                Is.EqualTo(new[] { VertexFormat.Float32x3, VertexFormat.Float32x3, VertexFormat.Float32x2, VertexFormat.Float32x4 }));

            // The G-buffer writes four color targets.
            Assert.That(info.FragmentOutputCount, Is.EqualTo(4));

            // The surface's parameter blocks are discovered by the [MaterialParams]
            // marker, not by name; member types and byte offsets come from Slang's
            // module-level reflection — no entry points, no link, no probe compile
            // of a pass template. The unmarked _globalRenderData block is engine
            // data and stays out.
            IReadOnlyDictionary<string, IReadOnlyList<SlangUniformMember>> layouts =
                compiler.Composer.GetParamsLayouts("parameterized_surface");
            Assert.That(layouts.Keys, Is.EqualTo(new[] { "PulseParams" }));
            IReadOnlyList<SlangUniformMember> members = layouts["PulseParams"];
            Assert.That(members.Select(member => (member.Name, member.OffsetBytes, member.FloatComponentCount)),
                Is.EqualTo(new[]
                {
                    ("pulseSpeed", 0u, 1),
                    ("pulseIntensity", 4u, 1),
                    ("pulseColor", 16u, 3),
                    ("bandFrequency", 28u, 1),
                }), "Scalar and vector members pack naturally; offsets are Slang's.");
        });
    }

    [Test]
    public void ShadowPassSpecializesAlphaTestByValue()
    {
        using GameEngine engine = new(GameEngineSetting.CreateNoGPU());
        using MaterialCompiler compiler = World3DAssetPipeline.CreateMaterialCompiler(engine.RenderingSystem);

        // The shadow template's alpha test is a value specialization parameter of
        // its fragment entry (<let AlphaTest : bool>) — the SHADOW_CUTOUT define's
        // replacement. Distinct values are distinct composed shaders.
        MaterialAsset asset = new() { Name = "parameterized", SurfaceShader = ParameterizedSurfacePath };
        Shader opaque = compiler.ComposeSurfaceShader(asset, "shadow_depth", ["false"]);
        Shader cutout = compiler.ComposeSurfaceShader(asset, "shadow_depth", ["true"]);
        ShaderModulesInfo plain = opaque.GetShaderModules();
        ShaderModulesInfo alphaTested = cutout.GetShaderModules();

        Assert.Multiple(() =>
        {
            Assert.That(cutout, Is.Not.SameAs(opaque));

            // The shadow template carries its cascade index as a push constant; the
            // engine reflects one range covering the float4 payload.
            Assert.That(plain.ReflectionInfo.PushConstantsSize, Is.EqualTo(16));
            Assert.That(alphaTested.ReflectionInfo.PushConstantsSize, Is.EqualTo(16));

            // The surface's explicit set-2 bindings stay in the layout across
            // specializations (specialization folds code, not explicit bindings),
            // so the binding side always sees the full surface resource set.
            ShaderResourceLocation plainAlbedo = GetResource(plain.ReflectionInfo, "_albedoTexture");
            ShaderResourceLocation cutoutAlbedo = GetResource(alphaTested.ReflectionInfo, "_albedoTexture");
            Assert.That((cutoutAlbedo.GroupIndex, cutoutAlbedo.Binding),
                Is.EqualTo((plainAlbedo.GroupIndex, plainAlbedo.Binding)),
                "The albedo texture keeps its binding across specializations.");

            // The fold itself is real: only the alpha-tested specialization samples
            // the surface, so its pixel module dwarfs the plain depth write.
            Assert.That(alphaTested.FragmentShader.GetValueOrDefault().Source.Length,
                Is.GreaterThan(plain.FragmentShader.GetValueOrDefault().Source.Length),
                "AlphaTest keeps the surface's texture sampling alive in the pixel stage.");
        });
    }

    [Test]
    public void SlangSurfaceComposesThroughTheMaterialCompiler()
    {
        using GameEngine engine = new(GameEngineSetting.CreateNoGPU());
        AssetSystem assets = engine.AssetSystem;
        World3DAssetPipeline.RegisterLoaders(assets, engine.RenderingSystem);

        using MaterialCompiler compiler = World3DAssetPipeline.CreateMaterialCompiler(engine.RenderingSystem);
        // The renderer's constructor registers itself as the "gbuffer" pass (template
        // × asset surface, the renderer's factory as the pass state).
        using GBufferRenderer gbuffer = new(engine.RenderingSystem, compiler);

        // The test surface with all four mixed-type parameters set.
        PbrMaterialAsset parameterized = new()
        {
            Name = "parameterized",
            SurfaceShader = ParameterizedSurfacePath,
            Parameters = new Dictionary<string, float[]>
            {
                ["pulseSpeed"] = [1.5f],
                ["pulseIntensity"] = [2.0f],
                ["pulseColor"] = [1.0f, 0.6f, 0.2f],
                ["bandFrequency"] = [4.0f],
            },
        };
        GraphicsMaterial material = compiler.Get(parameterized, "gbuffer");

        Assert.Multiple(() =>
        {
            Assert.That(compiler.Get(parameterized, "gbuffer"), Is.SameAs(material), "Composed Slang materials cache per (asset, pass).");
            Assert.That(material.TryGetResourceId("PulseParams", out _), Is.True,
                "The surface's parameter block binds by its (free) name.");
            Assert.That(material.TryGetResourceId("_albedoTexture", out _), Is.True,
                "The surface's textures bind by name.");
            Assert.That(material.TryGetResourceId(ShaderResourceId.Camera, out _), Is.True,
                "The template's camera binding survives.");
            Assert.That(material.TryGetResourceId(ShaderResourceId.GlobalRenderData, out _), Is.True,
                "The surface's _globalRenderData declaration reaches the compiled shader.");
        });

        // Unknown parameter names fail loudly (typo in the asset).
        PbrMaterialAsset typo = new()
        {
            Name = "typo",
            SurfaceShader = ParameterizedSurfacePath,
            Parameters = new Dictionary<string, float[]> { ["nonsense"] = [1.0f] },
        };
        Assert.That(() => compiler.Get(typo, "gbuffer"), Throws.TypeOf<InvalidDataException>());

        // A parameter wider than its reflected member is a mismatch, not padding.
        PbrMaterialAsset tooWide = new()
        {
            Name = "tooWide",
            SurfaceShader = ParameterizedSurfacePath,
            Parameters = new Dictionary<string, float[]> { ["pulseSpeed"] = [1.0f, 2.0f] },
        };
        Assert.That(() => compiler.Get(tooWide, "gbuffer"), Throws.TypeOf<InvalidDataException>());

        // A game-registered pass composes like the built-in ones: open registration
        // is the extension point (here: a minimal materializing factory).
        compiler.RegisterPass(new StubMaterialPass("glass", "glass", engine.RenderingSystem));
        PbrMaterialAsset glass = new()
        {
            Name = "glass",
            SurfaceShader = ParameterizedSurfacePath,
        };
        GraphicsMaterial glassMaterial = compiler.Get(glass, "glass");
        Assert.That(glassMaterial.TryGetResourceId(ShaderResourceId.Camera, out _), Is.True,
            "The glass template declares the camera binding.");
    }

    private static void AssertResource(
        ShaderReflectionInfo info,
        string name,
        int group,
        uint binding,
        BindingType type)
    {
        ShaderResourceLocation location = GetResource(info, name);
        Assert.That(location.GroupIndex, Is.EqualTo(group), $"{name} descriptor set");
        Assert.That(location.Binding, Is.EqualTo(binding), $"{name} binding");
        Assert.That(location.Type, Is.EqualTo(type), $"{name} binding type");
    }

    private static ShaderResourceLocation GetResource(ShaderReflectionInfo info, string name)
    {
        Assert.That(info.TryGetResourceLocation(name, out ShaderResourceLocation location),
            Is.True, $"Missing reflected resource {name}");
        return location;
    }

    private static void AssertLayoutEntry(
        ShaderReflectionInfo info, string name, int group, uint binding, BindingType type)
    {
        (int GroupIndex, BindGroupEntry Entry)? found = null;
        for (int groupIndex = 0; groupIndex < info.BindGroups.Count; groupIndex++)
        {
            foreach (BindGroupEntryInfo entryInfo in info.BindGroups[groupIndex].Bindings)
            {
                if (entryInfo.Entry.Name == name)
                {
                    found = (groupIndex, entryInfo.Entry);
                }
            }
        }
        Assert.That(found, Is.Not.Null, $"Missing bind group entry {name}");
        Assert.That(found!.Value.GroupIndex, Is.EqualTo(group), $"{name} descriptor set");
        Assert.That(found.Value.Entry.Binding, Is.EqualTo(binding), $"{name} binding");
        Assert.That(found.Value.Entry.Type, Is.EqualTo(type), $"{name} binding type");
    }
}
