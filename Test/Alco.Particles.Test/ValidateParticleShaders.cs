using NUnit.Framework;
using Alco.Engine;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.Particles.Test;

/// <summary>
/// Compiles every shader module shipped with the Alco.Particles module through the
/// shared slang module system (the same compiler path used at runtime), and — the
/// part the plain module sweep cannot cover — composes the generic emit/simulate
/// pass templates with the built-in default behaviors AND with custom behavior
/// modules (Files/Assets/Shaders/TestBehavior{2D,3D}.slang), linking and generating
/// code for each composition.
/// </summary>
public class ValidateParticleShaders
{
    /// <summary>
    /// Uses the plain no-GPU setting (shader cache disabled): this test must exercise real
    /// slang compilation, so a cached hit would defeat its purpose.
    /// </summary>
    public GameEngineSetting Setting = GameEngineSetting.CreateNoGPU();

    /// <summary>The minimal engine host for the shader system.</summary>
    public class ShaderValidator(GameEngineSetting setting) : GameEngine(setting);

    [Test(Description = "Validate all Alco.Particles shader modules")]
    public void ValidateAllParticleShaders()
    {
        using ShaderValidator engine = new(Setting);
        // Every .slang file of the module's shader tree compiles; the load name
        // is the file stem (docs/SlangCodingStandard.md). Test fixture behavior
        // modules count too.
        string[] files = engine.AssetSystem.AllAssetNames
            .Where(x => x.EndsWith(".slang", StringComparison.OrdinalIgnoreCase)
                && (x.Contains("Particle") || x.Contains("TestBehavior")))
            .ToArray();

        Assert.That(files, Is.Not.Empty,
            "No Alco.Particles shader modules were found; the module assets failed to flow into the test output.");

        try
        {
            foreach (string file in files)
            {
                string moduleName = Path.GetFileNameWithoutExtension(file);
                var (entryCount, anyGeneric) = engine.RenderingSystem.ShaderSystem.Modules
                    .GetOrLoadModule(moduleName).GetEntryPointInfo();
                if (entryCount > 0 && !anyGeneric)
                {
                    // Non-generic entry modules (the init passes) additionally
                    // link unspecialized; the generic render pass templates link
                    // through the composition test below.
                    _ = engine.RenderingSystem.ShaderSystem.GetShader(moduleName).GetShaderModules();
                }
            }
        }
        catch (Exception e)
        {
            Assert.Fail($"Failed to load shader: {e}");
        }
    }

    [Test(Description = "Compose the emit/simulate templates with default and custom behaviors")]
    public void ComposeSimulationTemplates()
    {
        using ShaderValidator engine = new(Setting);
        ShaderSystem shaderSystem = engine.RenderingSystem.ShaderSystem;
        using var compiler = new MaterialCompiler(engine.RenderingSystem);

        foreach (string dimension in new[] { "2D", "3D" })
        {
            ShaderLibrary emit = shaderSystem.GetLibrary($"GpuParticleEmit{dimension}");
            ShaderLibrary simulate = shaderSystem.GetLibrary($"GpuParticleSimulate{dimension}");
            foreach (string behavior in new[] { $"AlcoParticles-Default{dimension}", $"TestBehavior{dimension}" })
            {
                ShaderLibrary behaviorLibrary = shaderSystem.GetLibrary(behavior);
                // Link + layout + codegen for both compositions.
                ShaderReflection emitReflection = compiler.ComposeCompute(emit, behaviorLibrary)
                    .GetShaderModules().ReflectionInfo;
                ShaderReflection simulateReflection = compiler.ComposeCompute(simulate, behaviorLibrary)
                    .GetShaderModules().ReflectionInfo;

                Assert.That(simulateReflection.TryGetResourceLocation("particles", out _), Is.True,
                    $"GpuParticleSimulate{dimension} × {behavior}: missing 'particles'");
                Assert.That(simulateReflection.TryGetResourceLocation("emitters", out _), Is.True,
                    $"GpuParticleSimulate{dimension} × {behavior}: missing 'emitters'");
                Assert.That(simulateReflection.TryGetResourceLocation("renderList", out _), Is.True,
                    $"GpuParticleSimulate{dimension} × {behavior}: missing 'renderList'");
                Assert.That(simulateReflection.TryGetResourceLocation("drawArgs", out _), Is.True,
                    $"GpuParticleSimulate{dimension} × {behavior}: missing 'drawArgs'");
                Assert.That(simulateReflection.TryGetResourceLocation("instanceData", out _), Is.True,
                    $"GpuParticleSimulate{dimension} × {behavior}: missing 'instanceData'");
                Assert.That(emitReflection.TryGetResourceLocation("particles", out _), Is.True,
                    $"GpuParticleEmit{dimension} × {behavior}: missing 'particles'");
                Assert.That(emitReflection.TryGetResourceLocation("drawArgs", out _), Is.True,
                    $"GpuParticleEmit{dimension} × {behavior}: missing 'drawArgs'");
            }
        }
    }

    [Test(Description = "Compose the render pass templates with the default and a custom surface")]
    public void ValidateRenderPassReflection()
    {
        using ShaderValidator engine = new(Setting);
        ShaderSystem shaderSystem = engine.RenderingSystem.ShaderSystem;
        using var compiler = new MaterialCompiler(
            engine.RenderingSystem, shaderSystem.GetLibrary(ParticleAssetPipeline.DefaultSurface));

        // The render passes are generic pass templates now: the resource contract
        // shows in the composed (template × surface) reflection, not in a plain
        // module link. The default surface covers the "texture" slot; the custom
        // fixture surface (TestParticleSurface) covers the .amat surface path.
        foreach (string module in new[] { ParticleAssetPipeline.RenderModule2D, ParticleAssetPipeline.RenderModule3D })
        {
            ShaderReflection reflection = compiler.ComposeSurfaceShader(null, shaderSystem.GetLibrary(module))
                .GetShaderModules().ReflectionInfo;
            Assert.Multiple(() =>
            {
                Assert.That(reflection.TryGetResourceLocation("camera", out _), Is.True, $"{module}: missing 'camera'");
                Assert.That(reflection.TryGetResourceLocation("particles", out _), Is.True, $"{module}: missing 'particles'");
                Assert.That(reflection.TryGetResourceLocation("emitters", out _), Is.True, $"{module}: missing 'emitters'");
                Assert.That(reflection.TryGetResourceLocation("texture", out _), Is.True, $"{module}: missing 'texture'");
                Assert.That(reflection.TryGetResourceLocation("colorGradient", out _), Is.True, $"{module}: missing 'colorGradient'");
                Assert.That(reflection.TryGetResourceLocation("sizeCurve", out _), Is.True, $"{module}: missing 'sizeCurve'");
            });
            // The batching contract: the vertex input splits into the mesh buffer
            // (vertex-step) and the simulate-written per-draw records — the
            // instance-step "drawData" field, one GpuInstanceData record (8 bytes,
            // two uints) per particle instance fetched through firstInstance.
            Assert.That(reflection.VertexLayouts.Count, Is.EqualTo(2), $"{module}: vertex layout count");
            Assert.That(reflection.VertexLayouts[0].StepMode, Is.EqualTo(VertexStepMode.Vertex), $"{module}: mesh layout must be vertex-step");
            Assert.That(reflection.VertexLayouts[1].StepMode, Is.EqualTo(VertexStepMode.Instance), $"{module}: drawData layout must be instance-step");
            Assert.That(reflection.VertexLayouts[1].Stride, Is.EqualTo(8u), $"{module}: drawData stride");
            Assert.That(reflection.VertexLayouts[1].Elements.Length, Is.EqualTo(1), $"{module}: drawData element count");
            Assert.That(reflection.VertexLayouts[1].Elements[0].Format, Is.EqualTo(VertexFormat.Uint32x2), $"{module}: drawData format");
            Assert.That(reflection.VertexLayouts[1].Elements[0].Name, Is.EqualTo("drawData"), $"{module}: drawData element name");
        }

        ShaderReflection customReflection = compiler.ComposeGraphics(
                shaderSystem.GetLibrary(ParticleAssetPipeline.RenderModule2D),
                shaderSystem.GetLibrary("TestParticleSurface"))
            .GetShaderModules().ReflectionInfo;
        Assert.Multiple(() =>
        {
            Assert.That(customReflection.TryGetResourceLocation("texture", out _), Is.True, "custom surface: missing 'texture'");
            Assert.That(customReflection.TryGetResourceLocation("noiseTexture", out _), Is.True, "custom surface: missing 'noiseTexture'");
            Assert.That(customReflection.TryGetResourceLocation("dissolveParams", out _), Is.True, "custom surface: missing 'dissolveParams'");
        });
    }
}
