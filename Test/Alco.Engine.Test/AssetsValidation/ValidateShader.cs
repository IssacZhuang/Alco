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

    [Test(Description = "Validate all shipped slang files compile")]
    public void ValidateAllShaders()
    {
        using ShaderValidator engine = new ShaderValidator(Setting);
        // Every .slang file compiles — slang has no hlsl/hlsli split: libraries,
        // generic entry-point modules and plain modules all load as modules, and
        // loading runs the full front-end (parsing, type checking, IR generation
        // and validation) over every branch of every generic body, independent
        // of any specialization arguments (link-time specialization).
        //
        // The load name is the dashed file stem (docs/SlangCodingStandard.md).
        // Modules with non-generic entry points additionally link unspecialized
        // through the GetShaderModules route (link + layout + codegen); generic
        // modules cannot link unspecialized — one representative specialization
        // each is validated by ValidateSlangModules (Alco.Rendering.Test).
        var files = engine.AssetSystem.AllAssetNames
            .Where(x => x.EndsWith(".slang", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.That(files, Is.Not.Empty,
            "No Slang shader modules were found; built-in shader assets failed to reach the test output.");

        try
        {
            foreach (string file in files)
            {
                string moduleName = Path.GetFileNameWithoutExtension(file).Replace('_', '-');
                var (entryCount, anyGeneric) = engine.RenderingSystem.ShaderSystem.Modules
                    .GetOrLoadModule(moduleName).GetEntryPointInfo();

                if (entryCount > 0 && !anyGeneric)
                {
                    _ = engine.RenderingSystem.ShaderSystem.GetShader(moduleName).GetShaderModules();
                }
            }
        }
        catch (Exception e)
        {
            Assert.Fail($"Failed to load shader: {e}");
        }
    }
}
