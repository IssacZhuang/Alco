using System.IO;
using System.Text;
using NUnit.Framework;
using Alco.Engine;
using Alco.Graphics;
using Alco.IO;
using Alco.Rendering;

namespace Alco.World3D.Test;

/// <summary>
/// The Slang material path of <see cref="MaterialCompiler"/>: hand-written P/Invoke
/// over the Slang C API compiling the module's Slang pass templates with surface
/// modules (interface-checked generic instantiation), Slang's own reflection translated
/// into the engine's shader reflection, and the reflection-driven mapping of mixed-type
/// <c>_materialParams</c> blocks. Uses a NoGPU engine with the module's real Slang
/// sources, mirroring <see cref="TestMaterialCompiler"/>; the engine's DXC toolchain is
/// not involved in any of these compiles.
/// </summary>
public class TestSlangMaterialCompiler
{
    /// <summary>
    /// Serve a Slang module/import/include request from the module's Slang source
    /// folders in the asset system (the same lookup MaterialCompiler's resolver does).
    /// </summary>
    private static string? TryReadSlangAsset(AssetSystem assets, string path)
    {
        string fileName = Path.GetFileName(path);
        foreach (string folder in new[] { "Pipelines/", "Materials/", "Libs/" })
        {
            if (assets.TryGetStream("ShadersSlang/" + folder + fileName, out Stream? stream))
            {
                using StreamReader reader = new(stream, Encoding.UTF8);
                return reader.ReadToEnd();
            }
        }
        return null;
    }

    [Test]
    public void TrivialShaderCompilesWithoutAFileSystem()
    {
        // Bisects the bridge: a self-contained shader touching neither the file
        // system callback nor modules, so any failure here is the session/request/
        // entry-point plumbing, not asset resolution.
        string source = """
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
        using SlangShaderCompiler slang = new();
        SlangCompiledShader compiled = slang.CompileGraphics(
            "trivial", source, Array.Empty<(string, string)>(), [],
            _ => null);

        Assert.Multiple(() =>
        {
            Assert.That(BitConverter.ToUInt32(compiled.VertexSpirv, 0), Is.EqualTo(0x07230203u));
            Assert.That(BitConverter.ToUInt32(compiled.FragmentSpirv, 0), Is.EqualTo(0x07230203u));
            Assert.That(compiled.Reflection.BindGroups.Count, Is.EqualTo(0));
        });
    }

    [Test]
    public void BuiltInPbrSurfaceCompilesAsNativeSlang()
    {
        using GameEngine engine = new(GameEngineSetting.CreateNoGPU());
        AssetSystem assets = engine.AssetSystem;
        using SlangShaderCompiler slang = new();

        string wrapper = MaterialCompiler.BuildSlangWrapper("gbuffer", "pbr_standard");
        SlangCompiledShader compiled = slang.CompileGraphics(
            "gbuffer+pbr_standard", wrapper, Array.Empty<(string, string)>(), [],
            path => TryReadSlangAsset(assets, path));

        Dictionary<string, BindGroupEntryInfo> entries = compiled.Reflection.BindGroups[0].Bindings
            .ToDictionary(entry => entry.Entry.Name, entry => entry);
        Assert.Multiple(() =>
        {
            Assert.That(BitConverter.ToUInt32(compiled.VertexSpirv, 0), Is.EqualTo(0x07230203u));
            Assert.That(BitConverter.ToUInt32(compiled.FragmentSpirv, 0), Is.EqualTo(0x07230203u));
            Assert.That(entries.Keys, Is.SupersetOf(new[]
            {
                "_albedoTexture", "_normalTexture", "_metallicRoughnessTexture", "_emissiveTexture",
            }));
        });
    }

    [Test]
    public void SlangReflectionReportsTheEngineLayout()
    {
        using GameEngine engine = new(GameEngineSetting.CreateNoGPU());
        AssetSystem assets = engine.AssetSystem;
        using SlangShaderCompiler slang = new();

        // The real wrapper the material compiler generates for the G-buffer template
        // with the test surface (mixed-type parameter block).
        string wrapper = MaterialCompiler.BuildSlangWrapper("gbuffer", "parameterized_surface");
        SlangCompiledShader compiled = slang.CompileGraphics(
            "gbuffer+parameterized_surface", wrapper, Array.Empty<(string, string)>(), ["_materialParams"],
            path => TryReadSlangAsset(assets, path));

        Assert.Multiple(() =>
        {
            // Both entry points produced SPIR-V modules.
            Assert.That(BitConverter.ToUInt32(compiled.VertexSpirv, 0), Is.EqualTo(0x07230203u));
            Assert.That(BitConverter.ToUInt32(compiled.FragmentSpirv, 0), Is.EqualTo(0x07230203u));

            ShaderReflectionInfo info = compiled.Reflection;

            // One bind group (set 0) holding every parameter; names address them.
            Assert.That(info.BindGroups.Count, Is.EqualTo(1));
            Dictionary<string, BindGroupEntryInfo> entries = info.BindGroups[0].Bindings
                .ToDictionary(entry => entry.Entry.Name, entry => entry);
            Assert.That(entries["_camera"].Entry.Type, Is.EqualTo(BindingType.UniformBuffer));
            Assert.That(entries["_camera"].Size, Is.EqualTo(64u), "One float4x4 view-projection.");
            Assert.That(entries["_instances"].Entry.Type, Is.EqualTo(BindingType.StorageBuffer));
            Assert.That(entries["_materialParams"].Entry.Type, Is.EqualTo(BindingType.UniformBuffer));
            Assert.That(entries["_materialParams"].Size, Is.EqualTo(32u), "4+4+12+4 with std140 padding.");
            Assert.That(entries["_globalRenderData"].Entry.Type, Is.EqualTo(BindingType.UniformBuffer));
            Assert.That(entries["_albedoTexture"].Entry.Type, Is.EqualTo(BindingType.Texture));
            Assert.That(entries["_albedoTextureSampler"].Entry.Type, Is.EqualTo(BindingType.Sampler));
            Assert.That(entries["_normalTexture"].Entry.Type, Is.EqualTo(BindingType.Texture));
            Assert.That(entries["_metallicRoughnessTexture"].Entry.Type, Is.EqualTo(BindingType.Texture));
            Assert.That(entries.Keys, Is.SupersetOf(new[]
            {
                "_camera", "_instances", "_materialParams", "_globalRenderData",
                "_albedoTexture", "_albedoTextureSampler", "_normalTexture", "_normalTextureSampler",
                "_metallicRoughnessTexture", "_metallicRoughnessTextureSampler",
            }), "Slang assigns all parameters to set 0; the engine addresses them by name.");

            // Bindings within the group are sorted and unique.
            List<uint> bindings = info.BindGroups[0].Bindings.Select(entry => entry.Entry.Binding).ToList();
            Assert.That(bindings, Is.EqualTo(bindings.Distinct().ToList()), "Binding indices are unique.");

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

            // The surface's mixed-type parameter block: member types and byte offsets
            // come from Slang's own reflection - the mapping the material compiler
            // writes parameters at (no more regex parsing of the source).
            List<SlangUniformMember> members = compiled.UniformMembers["_materialParams"];
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
    public void SlangTemplatesReportPushConstantsAndHonorDefines()
    {
        using GameEngine engine = new(GameEngineSetting.CreateNoGPU());
        AssetSystem assets = engine.AssetSystem;
        using SlangShaderCompiler slang = new();

        string wrapper = MaterialCompiler.BuildSlangWrapper("shadow_depth", "parameterized_surface");

        SlangCompiledShader plain = slang.CompileGraphics(
            "shadow_depth+parameterized_surface", wrapper, Array.Empty<(string, string)>(), ["_materialParams"],
            path => TryReadSlangAsset(assets, path));
        SlangCompiledShader cutout = slang.CompileGraphics(
            "shadow_depth+parameterized_surface+SHADOW_CUTOUT", wrapper, [("SHADOW_CUTOUT", "1")], ["_materialParams"],
            path => TryReadSlangAsset(assets, path));

        Assert.Multiple(() =>
        {
            // The shadow template carries its cascade index as a push constant; the
            // engine reflects one range covering the float4 payload.
            Assert.That(plain.Reflection.PushConstantsSize, Is.EqualTo(16));
            Assert.That(cutout.Reflection.PushConstantsSize, Is.EqualTo(16));

            // The program layout is the union of both entry points, so the surface's
            // resources stay at stable bindings across permutations (Slang assigns
            // per-permutation binding numbers that would otherwise shift).
            Dictionary<string, BindGroupEntryInfo> plainEntries = plain.Reflection.BindGroups[0].Bindings
                .ToDictionary(entry => entry.Entry.Name, entry => entry);
            Dictionary<string, BindGroupEntryInfo> cutoutEntries = cutout.Reflection.BindGroups[0].Bindings
                .ToDictionary(entry => entry.Entry.Name, entry => entry);
            Assert.That(plainEntries["_albedoTexture"].Entry.Binding,
                Is.EqualTo(cutoutEntries["_albedoTexture"].Entry.Binding),
                "The albedo texture keeps its binding across permutations.");
            Assert.That(plainEntries.ContainsKey("_materialParams"), Is.True,
                "The parameter block stays bound in every permutation (unbound slots default to white).");

            // Preprocessor defines reach the template through the wrapper translation
            // unit: only the cutout permutation evaluates the surface, so its pixel
            // module dwarfs the plain depth write.
            Assert.That(cutout.FragmentSpirv.Length, Is.GreaterThan(plain.FragmentSpirv.Length * 4),
                "SHADOW_CUTOUT keeps the surface's texture sampling alive in the pixel stage.");
        });
    }

    [Test]
    public void SlangSurfaceComposesThroughTheMaterialCompiler()
    {
        using GameEngine engine = new(GameEngineSetting.CreateNoGPU());
        AssetSystem assets = engine.AssetSystem;
        World3DAssetPipeline.RegisterLoaders(assets, engine.RenderingSystem);

        Shader gbufferShader = assets.Load<Shader>(World3DAssetPaths.Shader_GBuffer);
        using GBufferRenderer gbuffer = new(engine.RenderingSystem, gbufferShader);
        using MaterialCompiler compiler = new(engine.RenderingSystem, assets);
        GBufferMaterialPass gbufferPass = new(gbuffer);
        compiler.RegisterPass(gbufferPass);

        // The test surface with all four mixed-type parameters set.
        MaterialAsset parameterized = new()
        {
            Name = "parameterized",
            SurfaceShader = "ShadersSlang/Materials/parameterized_surface.slang",
            Parameters = new Dictionary<string, float[]>
            {
                ["pulseSpeed"] = [1.5f],
                ["pulseIntensity"] = [2.0f],
                ["pulseColor"] = [1.0f, 0.6f, 0.2f],
                ["bandFrequency"] = [4.0f],
            },
        };
        GraphicsMaterial material = compiler.Get(parameterized, gbufferPass);

        Assert.Multiple(() =>
        {
            Assert.That(compiler.Get(parameterized, gbufferPass), Is.SameAs(material), "Composed Slang materials cache per (asset, pass).");
            Assert.That(material.TryGetResourceId("_materialParams", out _), Is.True,
                "The surface's parameter block binds by name.");
            Assert.That(material.TryGetResourceId("_albedoTexture", out _), Is.True,
                "The surface's textures bind by name.");
            Assert.That(material.TryGetResourceId(ShaderResourceId.Camera, out _), Is.True,
                "The template's camera binding survives.");
            Assert.That(material.TryGetResourceId(ShaderResourceId.GlobalRenderData, out _), Is.True,
                "The surface's _globalRenderData declaration reaches the compiled shader.");
        });

        // Unknown parameter names fail loudly (typo in the asset).
        MaterialAsset typo = new()
        {
            Name = "typo",
            SurfaceShader = "ShadersSlang/Materials/parameterized_surface.slang",
            Parameters = new Dictionary<string, float[]> { ["nonsense"] = [1.0f] },
        };
        Assert.That(() => compiler.Get(typo, gbufferPass), Throws.TypeOf<InvalidDataException>());

        // A parameter wider than its reflected member is a mismatch, not padding.
        MaterialAsset tooWide = new()
        {
            Name = "tooWide",
            SurfaceShader = "ShadersSlang/Materials/parameterized_surface.slang",
            Parameters = new Dictionary<string, float[]> { ["pulseSpeed"] = [1.0f, 2.0f] },
        };
        Assert.That(() => compiler.Get(tooWide, gbufferPass), Throws.TypeOf<InvalidDataException>());

        // A Slang surface composed with a pass that has no Slang template (glass)
        // fails with a clear message instead of splicing nonsense.
        MaterialAsset glass = new()
        {
            Name = "glass",
            SurfaceShader = "ShadersSlang/Materials/parameterized_surface.slang",
        };
        Assert.That(() => compiler.Get(glass, new TemplatePass("glass", World3DAssetPaths.Shader_ForwardGlass)),
            Throws.TypeOf<InvalidDataException>(), "The glass pass has no Slang counterpart yet.");
    }

    /// <summary>
    /// A minimal pass that only composes one template — enough to drive
    /// <see cref="MaterialCompiler"/>'s template dispatch without a renderer.
    /// </summary>
    private sealed class TemplatePass(string id, string template) : IMaterialPass
    {
        public string Id { get; } = id;

        public GraphicsMaterial Compile(MaterialCompileContext context)
        {
            context.ComposeShader(template);
            throw new InvalidOperationException("unreachable: composition throws first");
        }

        public void RebindTextures(MaterialCompileContext context, GraphicsMaterial material, IReadOnlyDictionary<string, Texture2D?> slots)
        {
        }
    }
}
