using NUnit.Framework;
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
