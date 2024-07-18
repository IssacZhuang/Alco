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

            foreach (string file in files)
            {
                try
                {
                    Shader shader = Assets.Load<Shader>(file);
                }
                catch (Exception e)
                {
                    Assert.Fail($"Failed to load shader {file}: {e}");
                }


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