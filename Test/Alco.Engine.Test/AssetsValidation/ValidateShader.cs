using NUnit.Framework;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.Engine.Test;

public class ValidateShader
{
    // Uses the plain NoGPU setting (shader cache disabled): this test must exercise
    // real Slang compilation, so a cached hit would defeat its purpose.
    public GameEngineSetting Setting = GameEngineSetting.CreateNoGPU();

    public class ShaderValidator : GameEngine
    {
        public ShaderValidator(GameEngineSetting setting) : base(setting)
        {

        }
    }

    [Test(Description = "Validate all shaders")]
    public void ValidateAllShaders()
    {
        using ShaderValidator engine = new ShaderValidator(Setting);
        var assets = engine.AssetSystem;
        // Query every entry-point module shipped by Alco.Rendering. Import-only
        // libraries live under ShadersSlang/Libs and are compiled by their importers.
        // Generic modules (fxaa, texture-compress-bc3) cannot be loaded
        // unspecialized — ValidateSlangModules (Alco.Rendering.Test) covers them
        // through their specialization argument table.
        string[] genericModules = ["fxaa.slang", "texture-compress-bc3.slang"];
        var files = assets.AllAssetNames
            .Where(x => x.EndsWith(".slang", StringComparison.OrdinalIgnoreCase))
            .Where(x => !x.Contains("ShadersSlang/Libs/", StringComparison.OrdinalIgnoreCase))
            .Where(x => !genericModules.Contains(Path.GetFileName(x), StringComparer.OrdinalIgnoreCase))
            .ToArray();

        Assert.That(files, Is.Not.Empty,
            "No Slang shader modules were found; built-in shader assets failed to reach the test output.");

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
