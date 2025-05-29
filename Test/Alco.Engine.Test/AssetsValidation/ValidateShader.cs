using NUnit.Framework;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.Engine.Test;

public class ValidateShader
{
    //used to check engine built-in shader
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
        //query all .hlsl files
        var files = assets.AllAssetNames.Where(x => x.EndsWith(".hlsl"));

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
            shader.TestAllDefines(OnTestPipleineError, OnTestPipleineSuccess);
        });

    }

    public static void OnTestPipleineError(string name, string[] defines, Exception e)
    {
        Assert.Fail($"Failed to compile shader: ({name}) with defines: [{string.Join(", ", defines)}]: {e}");
    }


    public static void OnTestPipleineSuccess(string name, string[] defines)
    {
        TestContext.WriteLine($"Successfully compiled shader: ({name}) with defines: [{string.Join(", ", defines)}]");
    }

}