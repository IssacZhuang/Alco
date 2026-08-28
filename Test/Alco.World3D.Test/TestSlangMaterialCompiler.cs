#nullable enable

using System.IO;
using System.Numerics;
using NUnit.Framework;
using Alco.Engine;
using Alco.Graphics;
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
/// <see cref="AssetPipeline.TestMaterialCompiler"/>.
/// </summary>
public class TestSlangMaterialCompiler
{
    /// <summary>The module name of this fixture's test surface (Files/Assets/Shaders/Materials/ParameterizedSurface.slang).</summary>
    private const string ParameterizedSurfaceModule = "ParameterizedSurface";

    /// <summary>The interned library reference of one module, as assets and passes hold it.</summary>
    private static ShaderLibrary Library(GameEngine engine, string moduleName)
        => engine.RenderingSystem.ShaderSystem.GetLibrary(moduleName);

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

        Shader shader = compiler.ComposeSurfaceShader(null, Library(engine, "GBuffer"));
        ShaderModulesInfo modules = shader.GetShaderModules();
        ShaderReflection info = modules.ReflectionInfo;

        Assert.Multiple(() =>
        {
            Assert.That(BitConverter.ToUInt32(modules.VertexShader.GetValueOrDefault().Source.ToArray(), 0), Is.EqualTo(0x07230203u));
            Assert.That(BitConverter.ToUInt32(modules.FragmentShader.GetValueOrDefault().Source.ToArray(), 0), Is.EqualTo(0x07230203u));

            // Set-scoped blocks: camera set 0, instances set 1,
            // surface resources set 2, the shared sampler bank set 3; bindings
            // inside a set are compiler-assigned.
            Assert.That(info.BindGroups.Count, Is.EqualTo(4));
            foreach (string name in new[]
                     {
                         "camera", "instances", "albedoTexture", "normalTexture",
                         "metallicRoughnessTexture", "emissiveTexture",
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
        // template with the test surface (mixed-type parameter block). Each
        // ParameterBlock owns one set: camera 0, instances 1, engine data 2,
        // the surface's marked block 3 (auto uniform buffer at binding 0, then
        // the textures flattened after it — no companion samplers), and the
        // shared sampler bank 4.
        MaterialAsset asset = new() { Name = "parameterized", Surface = Library(engine, ParameterizedSurfaceModule) };
        Shader shader = compiler.ComposeSurfaceShader(asset, Library(engine, "GBuffer"));
        ShaderReflection info = shader.GetShaderModules().ReflectionInfo;

        Assert.Multiple(() =>
        {
            AssertResource(info, "camera", 0, 0, BindingType.UniformBuffer);
            AssertResource(info, "instances", 1, 0, BindingType.StorageBuffer);
            AssertResource(info, "globalRenderData", 2, 0, BindingType.UniformBuffer);
            AssertResource(info, "pulseParams", 3, 0, BindingType.UniformBuffer);
            AssertResource(info, "albedoTexture", 3, 1, BindingType.Texture);
            AssertResource(info, "normalTexture", 3, 2, BindingType.Texture);
            AssertResource(info, "metallicRoughnessTexture", 3, 3, BindingType.Texture);

            // Samplers come from the shared bank (the _samplers block of
            // alco_rendering_core): the whole bank reflects as its own set and
            // every member resolves by name from the SharedSamplers.
            AssertLayoutEntry(info, "linearRepeat", 4, 1, BindingType.Sampler);
            AssertLayoutEntry(info, "depthComparison", 4, 8, BindingType.SamplerComparison);

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
            IReadOnlyDictionary<string, IReadOnlyList<ShaderUniformMember>> layouts =
                compiler.GetParamsLayouts(Library(engine, ParameterizedSurfaceModule));
            Assert.That(layouts.Keys, Is.EqualTo(new[] { "pulseParams" }));
            IReadOnlyList<ShaderUniformMember> members = layouts["pulseParams"];
            Assert.That(members.Select(member => (member.Name, member.OffsetBytes, member.ComponentCount)),
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
        MaterialAsset asset = new() { Name = "parameterized", Surface = Library(engine, ParameterizedSurfaceModule) };
        ShaderLibrary shadowTemplate = Library(engine, "ShadowDepth");
        Shader opaque = compiler.ComposeGraphics(shadowTemplate, asset.Surface!,
            new Dictionary<string, ShaderValue> { ["AlphaTest"] = false });
        Shader cutout = compiler.ComposeGraphics(shadowTemplate, asset.Surface!,
            new Dictionary<string, ShaderValue> { ["AlphaTest"] = true });
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
            ShaderResourceLocation plainAlbedo = GetResource(plain.ReflectionInfo, "albedoTexture");
            ShaderResourceLocation cutoutAlbedo = GetResource(alphaTested.ReflectionInfo, "albedoTexture");
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

        using MaterialCompiler compiler = World3DAssetPipeline.CreateMaterialCompiler(engine.RenderingSystem);
        // The renderer compiles its per-asset materials through the material
        // compiler (the template composes with the asset's surface, the factory
        // applies the pass state); the stateless factory beneath compiles fresh
        // materials per call.
        using GBufferRenderer gbuffer = new(
            engine.RenderingSystem, compiler, engine.RenderingSystem.ShaderSystem.GetLibrary("GBuffer"));

        // The test surface with all four mixed-type parameters set (a Vector4 reads
        // as many leading components as the reflected member takes).
        PbrMaterialAsset parameterized = new()
        {
            Name = "parameterized",
            Surface = Library(engine, ParameterizedSurfaceModule),
            Parameters = new Dictionary<string, ShaderValue>
            {
                ["pulseSpeed"] = new Vector4(1.5f, 0.0f, 0.0f, 0.0f),
                ["pulseIntensity"] = new Vector4(2.0f, 0.0f, 0.0f, 0.0f),
                ["pulseColor"] = new Vector4(1.0f, 0.6f, 0.2f, 0.0f),
                ["bandFrequency"] = new Vector4(4.0f, 0.0f, 0.0f, 0.0f),
            },
        };
        GraphicsMaterial material = gbuffer.GetMaterial(parameterized);

        Assert.Multiple(() =>
        {
            Assert.That(gbuffer.GetMaterial(parameterized), Is.SameAs(material),
                "The renderer's cache shares one material per asset.");
            Assert.That(material.TryGetResourceId("pulseParams", out _), Is.True,
                "The surface's parameter block binds by its (free) name.");
            Assert.That(material.TryGetResourceId("albedoTexture", out _), Is.True,
                "The surface's textures bind by name.");
            Assert.That(material.TryGetResourceId(ShaderResourceId.Camera, out _), Is.True,
                "The template's camera binding survives.");
            Assert.That(material.TryGetResourceId(ShaderResourceId.GlobalRenderData, out _), Is.True,
                "The surface's globalRenderData declaration reaches the compiled shader.");
        });

        // Unknown parameter names fail loudly (typo in the asset).
        PbrMaterialAsset typo = new()
        {
            Name = "typo",
            Surface = Library(engine, ParameterizedSurfaceModule),
            Parameters = new Dictionary<string, ShaderValue> { ["nonsense"] = new Vector4(1.0f, 0.0f, 0.0f, 0.0f) },
        };
        Assert.That(() => gbuffer.GetMaterial(typo), Throws.TypeOf<InvalidDataException>());

        // A game-defined facility composes like the built-in ones: its template and
        // factory are handed straight to the compiler (here: a minimal factory).
        PbrMaterialAsset glassAsset = new()
        {
            Name = "glass",
            Surface = Library(engine, ParameterizedSurfaceModule),
        };
        GraphicsMaterial glassMaterial = compiler.Compile(glassAsset,
            Library(engine, "Glass"),
            (a, shader) => engine.RenderingSystem.CreateGraphicsMaterial(shader, $"{a.Name}_glass"));
        Assert.That(glassMaterial.TryGetResourceId(ShaderResourceId.Camera, out _), Is.True,
            "The glass template declares the camera binding.");
    }

    private static void AssertResource(
        ShaderReflection info,
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

    private static ShaderResourceLocation GetResource(ShaderReflection info, string name)
    {
        Assert.That(info.TryGetResourceLocation(name, out ShaderResourceLocation location),
            Is.True, $"Missing reflected resource {name}");
        return location;
    }

    private static void AssertLayoutEntry(
        ShaderReflection info, string name, int group, uint binding, BindingType type)
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
