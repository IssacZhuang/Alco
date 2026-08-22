using NUnit.Framework;
using Alco.Graphics;
using Alco.Rendering;
using Alco.Engine;

namespace Alco.World3D.Test;

/// <summary>
/// Compiles every shader shipped with the Alco.World3D module (all define
/// combinations) through real DXC, mirroring the engine's own
/// <c>ValidateShader</c> test. The module's shaders and their engine-side
/// include libraries both flow into the test output's <c>Assets</c> folder,
/// so the default engine asset source serves them.
/// </summary>
public class ValidateShader
{
    // Uses the plain NoGPU setting (shader cache disabled): this test must exercise
    // real DXC compilation, so a cached hit would defeat its purpose.
    public GameEngineSetting Setting = GameEngineSetting.CreateNoGPU();

    public class ShaderValidator : GameEngine
    {
        public ShaderValidator(GameEngineSetting setting) : base(setting)
        {

        }
    }

    [Test(Description = "Validate all Alco.World3D shaders")]
    public void ValidateAllWorld3DShaders()
    {
        using ShaderValidator engine = new ShaderValidator(Setting);
        var assets = engine.AssetSystem;
        // Query every .hlsl file of the module's shader folder.
        var files = assets.AllAssetNames.Where(x => x.EndsWith(".hlsl") && x.StartsWith(World3DAssetPaths.Folder));

        Assert.That(files, Is.Not.Empty, "No World3D shaders were found; the module assets failed to flow into the test output.");

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

    [Test(Description = "Validate all Alco.World3D shaders through Slang")]
    public void ValidateAllWorld3DShadersWithSlang()
    {
        using ShaderValidator engine = new ShaderValidator(Setting);
        var assets = engine.AssetSystem;
        string[] files = assets.AllAssetNames
            .Where(x => x.EndsWith(".hlsl") && x.StartsWith(World3DAssetPaths.Folder))
            .ToArray();

        Assert.That(files, Is.Not.Empty,
            "No World3D shaders were found; the module assets failed to flow into the test output.");

        using SlangPipelineShaderFactory slang = new(engine.RenderingSystem, assets);
        foreach (string file in files)
        {
            Shader shader = slang.Load(file);
            shader.TestAllDefines(OnTestPipelineError, OnTestPipelineSuccess);
        }
    }

    [Test]
    public void SlangPipelineReflectionMatchesWorld3DResourceConventions()
    {
        using ShaderValidator engine = new ShaderValidator(Setting);
        using SlangPipelineShaderFactory slang = new(engine.RenderingSystem, engine.AssetSystem);

        ShaderReflectionInfo gbuffer = slang.Load(World3DAssetPaths.Shader_GBuffer)
            .GetShaderModules().ReflectionInfo;
        ShaderReflectionInfo hbao = slang.Load(
            "Shaders/Pipelines/Rendering/PBR/HBAO.hlsl")
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
