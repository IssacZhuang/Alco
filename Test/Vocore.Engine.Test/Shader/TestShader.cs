using NUnit.Framework;
using Vocore.Graphics;
using Vocore.Rendering;

namespace Vocore.Engine.Test;

public class TestShader
{
    //used to check engine built-in shader
    public GameEngineSetting Setting = GameEngineSetting.CreateNoGPU();

    public class ShaderValidator : GameEngine
    {
        public ShaderValidator(GameEngineSetting setting) : base(setting)
        {
            //query all .hlsl files
            var files = Assets.AllFileNames.Where(x => x.EndsWith(".hlsl"));

            List<Task<Shader>> tasks = new();

            foreach (string file in files)
            {
                tasks.Add(Assets.LoadAsyncTask<Shader>(file));
            }

            try{
                Task.WaitAll(tasks);
            }
            catch (Exception e)
            {
                Assert.Fail($"Failed to compile shader: {e}");
            }
        }
    }

    [Test(Description = "Test all shaders")]
    public void TestAllShaders()
    {
        Setting.RunOnce = true;
        new ShaderValidator(Setting);
    }
}