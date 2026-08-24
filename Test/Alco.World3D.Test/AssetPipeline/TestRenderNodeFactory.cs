using System.Text;
using System.Text.Json;
using NUnit.Framework;
using Alco.Engine;
using Alco.Graphics;
using Alco.IO;
using Alco.Rendering;
using Alco.World3D;

namespace Alco.World3D.Test;

/// <summary>
/// Parsing and loading of render node factory files (.rnfact): the <c>$type</c>
/// CLR-name discriminator selects the factory class (no registration — assembly
/// scan), shader references resolve typed through the shared shader system at
/// load time (a typoed module name fails at load with the file's context),
/// tunable properties map typed (floats, string enums) and unknown fields or
/// discriminators fail at load. The factory context's service blackboard is
/// type-keyed and reports missing dependencies by name. Uses a NoGPU engine
/// with registered virtual modules (module resolution and material creation
/// are device-independent).
/// </summary>
public class TestRenderNodeFactory
{
    private const string FxaaModuleSource = """
        module test_fxaa;

        cbuffer _pass : register(b0, space0)
        {
            Texture2D _texture;
            SamplerState _textureSampler;
        };

        struct Vertex { float3 position : POSITION; float2 uv : TEXCOORD0; };
        struct V2F { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };

        [shader("vertex")]
        V2F MainVS(Vertex input) { V2F o; o.position = float4(input.position, 1.0f); o.uv = input.uv; return o; }

        [shader("pixel")]
        float4 MainPS(V2F input) : SV_TARGET { return _texture.Sample(_textureSampler, input.uv); }
        """;

    private static JsonSerializerOptions CreateOptions(GameEngine engine)
    {
        return AssetLoaderRenderNodeFactory.CreateJsonOptions(engine.RenderingSystem.ShaderSystem);
    }

    private static RenderNodeFactory Parse(GameEngine engine, string json)
    {
        return JsonSerializer.Deserialize<RenderNodeFactory>(Encoding.UTF8.GetBytes(json), CreateOptions(engine))!;
    }

    private static GameEngine CreateEngine()
    {
        GameEngine engine = new(GameEngineSetting.CreateNoGPU());
        // Virtual modules so load-time shader resolution never touches disk.
        engine.RenderingSystem.ShaderSystem.GetShaderFromModule("test_fxaa", "test_fxaa.slang", FxaaModuleSource);
        return engine;
    }

    [Test]
    public void ParseResolvesShaderModulesTypedAtLoadTime()
    {
        using GameEngine engine = CreateEngine();
        RenderNodeFactory factory = Parse(engine, """
        {
            "$type": "Alco.Rendering.RGNodeFactory_FXAA",
            // jsonc: comments and trailing commas are author-friendly,
            "sceneCopyShader": "test_fxaa",
            "quality": "High",
        }
        """);

        var fxaa = (RGNodeFactory_FXAA)factory;
        Assert.Multiple(() =>
        {
            Assert.That(fxaa.SceneCopyShader, Is.Not.Null);
            Assert.That(fxaa.SceneCopyShader.Name, Is.EqualTo("test_fxaa"),
                "The reference resolves through the shader system at load time.");
            Assert.That(fxaa.Quality, Is.EqualTo(FXAAQuality.High));
        });
    }

    [Test]
    public void ParseFailsForUnknownShaderModuleAtLoadTime()
    {
        using GameEngine engine = CreateEngine();
        Assert.That(() => Parse(engine, """
            { "$type": "Alco.Rendering.RGNodeFactory_FXAA", "sceneCopyShader": "no_such_module" }
            """),
            Throws.TypeOf<JsonException>(),
            "A typoed module name fails at load, not at node creation.");
    }

    [Test]
    public void ParseMapsTunableFieldsAndOptionalShaderSlots()
    {
        using GameEngine engine = CreateEngine();
        RenderNodeFactory factory = Parse(engine, """
        {
            "$type": "Alco.World3D.RGNodeFactory_VoxelGI",
            "clearShader": "test_fxaa",
            "injectShader": "test_fxaa",
            "mipShader": "test_fxaa",
            "mipChainShader": "test_fxaa",
            "propagateShader": "test_fxaa",
            "traceShader": "test_fxaa",
            "demosaicShader": "test_fxaa",
            "blueNoiseShader": "test_fxaa",
            "resolution": 64,
            "traceResolutionScale": 0.75
        }
        """);

        var gi = (RGNodeFactory_VoxelGI)factory;
        Assert.Multiple(() =>
        {
            Assert.That(gi.ClearShader.Name, Is.EqualTo("test_fxaa"));
            Assert.That(gi.UpsampleShader, Is.Null, "An omitted optional shader slot stays null.");
            Assert.That(gi.Resolution, Is.EqualTo(64));
            Assert.That(gi.TraceResolutionScale, Is.EqualTo(0.75f));
        });
    }

    [Test]
    public void ParseRejectsMissingRequiredShader()
    {
        using GameEngine engine = CreateEngine();
        Assert.That(() => Parse(engine, """{ "$type": "Alco.Rendering.RGNodeFactory_Bloom" }"""),
            Throws.TypeOf<JsonException>(),
            "Required shader slots are part of the file, not code defaults.");
    }

    [Test]
    public void ParseRejectsUnknownDiscriminator()
    {
        using GameEngine engine = CreateEngine();
        Assert.That(() => Parse(engine, """{ "$type": "Alco.World3D.RGNodeFactory_Ghost" }"""),
            Throws.TypeOf<JsonException>());
    }

    [Test]
    public void ParseRejectsMissingDiscriminator()
    {
        using GameEngine engine = CreateEngine();
        // The base type is abstract: a file without $type cannot create anything
        // (System.Text.Json reports it as a NotSupportedException).
        Assert.That(() => Parse(engine, """{ "threshold": 1.0 }"""),
            Throws.TypeOf<NotSupportedException>());
    }

    [Test]
    public void ParseRejectsUnknownFields()
    {
        using GameEngine engine = CreateEngine();
        Assert.That(() => Parse(engine,
                """{ "$type": "Alco.World3D.RGNodeFactory_HBAO", "hbaoShader": "test_fxaa", "noSuchField": 1 }"""),
            Throws.TypeOf<JsonException>());
    }

    [Test]
    public void LoadRoundTripsThroughAssetSystem()
    {
        string directory = Path.Combine(Path.GetTempPath(), "alco_rnfact_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "Fxaa.rnfact"), """
                {
                    // FXAA node shader binding
                    "$type": "Alco.Rendering.RGNodeFactory_FXAA",
                    "sceneCopyShader": "test_fxaa",
                    "threshold": 0.2
                }
                """);

            using GameEngine engine = CreateEngine();
            AssetSystem assets = engine.AssetSystem;
            assets.AddFileSource(new DirectoryFileSource(directory));

            RenderNodeFactory first = assets.Load<RenderNodeFactory>("Fxaa.rnfact");
            RenderNodeFactory second = assets.Load<RenderNodeFactory>("Fxaa.rnfact");

            Assert.Multiple(() =>
            {
                Assert.That(first, Is.TypeOf<RGNodeFactory_FXAA>());
                Assert.That(((RGNodeFactory_FXAA)first).Threshold, Is.EqualTo(0.2f));
                Assert.That(second, Is.SameAs(first), "The asset system must cache factory assets per file.");
            });
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void LoadFailsForInvalidFile()
    {
        string directory = Path.Combine(Path.GetTempPath(), "alco_rnfact_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "Broken.rnfact"),
                """{ "$type": "Alco.World3D.RGNodeFactory_HBAO", "noSuchField": 1 }""");

            using GameEngine engine = CreateEngine();
            engine.AssetSystem.AddFileSource(new DirectoryFileSource(directory));

            Assert.That(() => engine.AssetSystem.Load<RenderNodeFactory>("Broken.rnfact"),
                Throws.TypeOf<AssetLoadException>(), "A typoed field fails at load, not at first use.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void ServicesResolveTypeKeyedAndReportMissing()
    {
        var services = new RenderNodeFactoryServices();
        var chain = new RenderChain();

        Assert.Multiple(() =>
        {
            Assert.That(services.Add(chain).Get<RenderChain>(), Is.SameAs(chain));
            Assert.That(services.TryGet<RenderChain>(), Is.SameAs(chain));
            Assert.That(services.TryGet<MaterialCompiler>(), Is.Null);
            Assert.That(() => services.Get<MaterialCompiler>(),
                Throws.InvalidOperationException.With.Message.Contains("MaterialCompiler"),
                "The error names the missing service type.");
        });
    }

    [Test]
    public void CreateBuildsNodeFromFactoryData()
    {
        using GameEngine engine = CreateEngine();
        RenderingSystem rendering = engine.RenderingSystem;
        using var graph = new RenderGraph(rendering, 64, 64, "rnfact_test");
        using var layout = rendering.GraphicsDevice.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            [new ColorAttachment(PixelFormat.RGBA16Float)], null, "test_post_layout"));
        var context = new RenderNodeFactoryContext(rendering, graph,
            new RenderNodeFactoryServices()
                .Add(new RenderChain())
                .Add(layout));

        var fxaa = (RGNodeFactory_FXAA)Parse(engine, """
            { "$type": "Alco.Rendering.RGNodeFactory_FXAA", "sceneCopyShader": "test_fxaa" }
            """);
        RGNode_FXAA node = fxaa.CreateNode<RGNode_FXAA>(context);

        Assert.Multiple(() =>
        {
            Assert.That(node, Is.Not.Null);
            Assert.That(node.Quality, Is.EqualTo(FXAAQuality.Medium), "Tunable defaults flow into the node.");
        });
    }

    [Test]
    public void CreateReportsMissingServicesByName()
    {
        using GameEngine engine = CreateEngine();
        var ssr = (RGNodeFactory_SSR)Parse(engine, """
            {
                "$type": "Alco.World3D.RGNodeFactory_SSR",
                "traceShader": "test_fxaa",
                "resolveShader": "test_fxaa",
                "compositeShader": "test_fxaa",
                "sceneCopyShader": "test_fxaa",
                "blueNoiseShader": "test_fxaa"
            }
            """);
        var context = new RenderNodeFactoryContext(engine.RenderingSystem,
            new RenderGraph(engine.RenderingSystem, 64, 64, "rnfact_test"));

        Assert.That(() => ssr.CreateNode<RGNode_SSR>(context),
            Throws.InvalidOperationException.With.Message.Contains("RenderChain"),
            "The first missing service the SSR factory needs is named in the error.");
    }
}
