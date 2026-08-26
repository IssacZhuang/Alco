using NUnit.Framework;
using Alco.Graphics;
using Alco.Rendering;
using Alco.Engine;
using Alco.IO;

namespace Alco.World3D.Test;

/// <summary>
/// Compiles every shader module shipped with the Alco.World3D module through the
/// shared slang module system (the same compiler path used at runtime). The
/// module's shader modules flow into the test output's <c>Assets</c> folder, so
/// the default engine asset source serves them; each file's dashed stem is its
/// load name. The four material-pass templates
/// (gbuffer/shadow_depth/rsm/glass) are surface-generic and define no entry
/// points — <see cref="TestSlangMaterialCompiler"/> covers their composition.
/// </summary>
public class ValidateShader
{
    // Uses the plain NoGPU setting (shader cache disabled): this test must exercise
    // real slang compilation, so a cached hit would defeat its purpose.
    public GameEngineSetting Setting = GameEngineSetting.CreateNoGPU();

    public class ShaderValidator : GameEngine
    {
        public ShaderValidator(GameEngineSetting setting) : base(setting)
        {
        }
    }

    [Test(Description = "Validate all Alco.World3D shader modules")]
    public void ValidateAllWorld3DShaders()
    {
        using ShaderValidator engine = new ShaderValidator(Setting);
        // Every .slang file of the module's shader tree compiles — slang has no
        // hlsl/hlsli split: import-only libraries, surface-generic pass templates
        // and value-generic entry modules all load as modules, and loading runs
        // the full front-end (parsing, type checking, IR generation and
        // validation) over every generic branch, independent of specialization
        // arguments. The load name is the dashed file stem
        // (docs/SlangCodingStandard.md).
        var files = engine.AssetSystem.AllAssetNames
            .Where(x => x.EndsWith(".slang") && x.StartsWith(World3DShaderModules.Folder))
            .ToArray();

        Assert.That(files, Is.Not.Empty, "No World3D shader modules were found; the module assets failed to flow into the test output.");

        // Modules with non-generic entry points additionally link unspecialized
        // through the GetShaderModules route (the same route every runtime
        // caller uses); generic modules cannot link unspecialized — the
        // surface-generic pass templates compose with a surface
        // (TestSlangMaterialCompiler) and the value-generic ones take one
        // representative specialization each (ValidateWorld3DSlangModules).
        List<Shader> shaders = [];
        try
        {
            foreach (string file in files)
            {
                string moduleName = Path.GetFileNameWithoutExtension(file);
                var (entryCount, anyGeneric) = engine.RenderingSystem.ShaderSystem.Modules
                    .GetOrLoadModule(moduleName).GetEntryPointInfo();
                if (entryCount > 0 && !anyGeneric)
                {
                    shaders.Add(engine.RenderingSystem.ShaderSystem.GetShader(moduleName));
                }
            }
        }
        catch (Exception e)
        {
            Assert.Fail($"Failed to load shader: {e}");
        }

        Parallel.ForEach(shaders, static shader => _ = shader.GetShaderModules());
    }

    [Test]
    public void ModuleReflectionMatchesWorld3DResourceConventions()
    {
        using ShaderValidator engine = new ShaderValidator(Setting);
        AssetSystem assets = engine.AssetSystem;

        // The G-buffer template composed with the built-in surface: one block
        // per set with its register space — camera set 0, instances set 1,
        // material resources set 2, then the shared sampler bank block — and
        // compiler-assigned bindings inside each block, resolved by name.
        using MaterialCompiler compiler = World3DAssetPipeline.CreateMaterialCompiler(engine.RenderingSystem);
        ShaderReflection gbuffer = compiler.ComposeSurfaceShader(null,
                engine.RenderingSystem.ShaderSystem.GetLibrary("gbuffer"))
            .GetShaderModules().ReflectionInfo;
        // The HBAO module name is factory-asset data now (Assets/RenderNodes/
        // HBAO.rnfact); the test pins it by its literal module name.
        ShaderReflection hbao = engine.RenderingSystem.ShaderSystem.GetShader("hbao")
            .GetShaderModules().ReflectionInfo;

        Assert.Multiple(() =>
        {
            Assert.That(gbuffer.BindGroups.Count, Is.EqualTo(4));
            AssertResource(gbuffer, "camera", 0, 0, BindingType.UniformBuffer);
            AssertResource(gbuffer, "instances", 1, 0, BindingType.StorageBuffer);
            AssertResource(gbuffer, "albedoTexture", 2, 0, BindingType.Texture);

            VertexInputLayout vertices = gbuffer.VertexLayouts.Single();
            Assert.That(vertices.Stride, Is.EqualTo(48u));
            Assert.That(vertices.Elements.Select(element => element.Offset),
                Is.EqualTo(new uint[] { 0, 12, 24, 32 }));

            // The hbao pass block is the root module's own ParameterBlock and so
            // takes set 0 (root-module blocks precede imported ones); it packs the
            // depth (member 0), normal (member 1) and AO output (member 2), while
            // the imported hbao-common _data block follows on set 1.
            AssertResource(hbao, "gbufferDepth", 0, 0, BindingType.Texture);
            ShaderResourceLocation depth = GetResource(hbao, "gbufferDepth");
            Assert.That(hbao.BindGroups[depth.GroupIndex].Bindings[depth.EntryIndex]
                .Entry.TextureInfo.SampleType, Is.EqualTo(TextureSampleType.Depth));

            AssertResource(hbao, "aoOutput", 0, 2, BindingType.StorageTexture);
            ShaderResourceLocation output = GetResource(hbao, "aoOutput");
            Assert.That(hbao.BindGroups[output.GroupIndex].Bindings[output.EntryIndex]
                .Entry.StorageTextureInfo.Format, Is.EqualTo(PixelFormat.RGBA16Float));
        });
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
}
