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
/// the default engine asset source serves them; the asset loader derives each
/// module's identity from its file name. The four material-pass templates
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
        var assets = engine.AssetSystem;
        // Query every entry-point-owning .slang module of the module's shader tree.
        var files = assets.AllAssetNames
            .Where(x => x.EndsWith(".slang") && x.StartsWith(World3DAssetPaths.Folder))
            .Where(IsEntryPointModule)
            .ToArray();

        Assert.That(files, Is.Not.Empty, "No World3D shader modules were found; the module assets failed to flow into the test output.");

        List<Task<Shader>> tasks = new();

        foreach (string file in files)
        {
            tasks.Add(assets.LoadAsync<Shader>(file));
        }

        try
        {
            Task.WaitAll(tasks);
        }
        catch (Exception e)
        {
            Assert.Fail($"Failed to load shader: {e}");
        }

        Parallel.ForEach(tasks, task =>
        {
            var shader = task.Result;
            shader.TestAllDefines(OnTestPipelineError, OnTestPipelineSuccess);
        });
    }

    [Test(Description = "Validate all Alco.World3D shader modules through direct module-system loads")]
    public void ValidateAllWorld3DShadersByModule()
    {
        using ShaderValidator engine = new ShaderValidator(Setting);
        var assets = engine.AssetSystem;
        string[] files = assets.AllAssetNames
            .Where(x => x.EndsWith(".slang") && x.StartsWith(World3DAssetPaths.Folder))
            .Where(IsEntryPointModule)
            .ToArray();

        Assert.That(files, Is.Not.Empty,
            "No World3D shader modules were found; the module assets failed to flow into the test output.");

        // The module-name keyed lookup path: the same route hot reload and the
        // material composition use (name → resolver → module system).
        foreach (string file in files)
        {
            string moduleName = Path.GetFileNameWithoutExtension(file);
            Shader shader = engine.RenderingSystem.ShaderSystem.GetShader(moduleName);
            shader.TestAllDefines(OnTestPipelineError, OnTestPipelineSuccess);
        }
    }

    [Test]
    public void ModuleReflectionMatchesWorld3DResourceConventions()
    {
        using ShaderValidator engine = new ShaderValidator(Setting);
        AssetSystem assets = engine.AssetSystem;

        // The G-buffer template composed with the built-in surface: explicit
        // source-declared bindings (plan D2) — camera set 0, instances set 1,
        // material resources set 2.
        using MaterialCompiler compiler = new(engine.RenderingSystem, assets);
        ShaderReflectionInfo gbuffer = compiler.GetTemplateShader(World3DAssetPaths.Shader_GBuffer)
            .GetShaderModules().ReflectionInfo;
        ShaderReflectionInfo hbao = engine.RenderingSystem.ShaderSystem.GetShader("HBAO")
            .GetShaderModules().ReflectionInfo;

        Assert.Multiple(() =>
        {
            Assert.That(gbuffer.BindGroups.Count, Is.EqualTo(3));
            AssertResource(gbuffer, "_camera", 0, 0, BindingType.UniformBuffer);
            AssertResource(gbuffer, "_instances", 1, 0, BindingType.StorageBuffer);
            AssertResource(gbuffer, "_albedoTexture", 2, 0, BindingType.Texture);

            VertexInputLayout vertices = gbuffer.VertexLayouts.Single();
            Assert.That(vertices.Stride, Is.EqualTo(48u));
            Assert.That(vertices.Elements.Select(element => element.Offset),
                Is.EqualTo(new uint[] { 0, 12, 24, 32 }));

            AssertResource(hbao, "_gbufferDepth", 1, 0, BindingType.Texture);
            ShaderResourceLocation depth = GetResource(hbao, "_gbufferDepth");
            Assert.That(hbao.BindGroups[depth.GroupIndex].Bindings[depth.EntryIndex]
                .Entry.TextureInfo.SampleType, Is.EqualTo(TextureSampleType.Depth));

            AssertResource(hbao, "_aoOutput", 3, 0, BindingType.StorageTexture);
            ShaderResourceLocation output = GetResource(hbao, "_aoOutput");
            Assert.That(hbao.BindGroups[output.GroupIndex].Bindings[output.EntryIndex]
                .Entry.StorageTextureInfo.Format, Is.EqualTo(PixelFormat.RGBA16Float));
        });
    }

    /// <summary>
    /// Whether a World3D shader asset can own entry points: the import-only trees
    /// (Libs, Materials, the alco-world3d-* libs converted from .slang) and the
    /// four surface-generic pass templates define none — their files are excluded
    /// from entry-point validation (see <see cref="TestSlangMaterialCompiler"/> and
    /// <see cref="ValidateWorld3DSlangModules.LibModule_Loads"/> for their coverage).
    /// </summary>
    private static bool IsEntryPointModule(string assetPath)
    {
        string fileName = Path.GetFileName(assetPath);
        if (assetPath.Contains("ShadersSlang/Libs/", StringComparison.OrdinalIgnoreCase) ||
            assetPath.Contains("ShadersSlang/Materials/", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("alco-world3d-", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return fileName is not ("gbuffer.slang" or "rsm.slang" or "shadow-depth.slang" or "glass.slang");
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

    public static void OnTestPipelineError(string name, string[] defines, Exception e)
    {
        Assert.Fail($"Failed to compile shader: ({name}) with defines: [{string.Join(", ", defines)}]: {e}");
    }


    public static void OnTestPipelineSuccess(string name, string[] defines)
    {
        // Intentionally not logged: the success path fires once per define
        // combination and would dump hundreds of lines into the test output.
    }

}
