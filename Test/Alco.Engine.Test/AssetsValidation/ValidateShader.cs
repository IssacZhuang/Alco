using NUnit.Framework;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.Engine.Test;

/// <summary>Tests that all shipped Slang shaders compile.</summary>
public class ValidateShader
{
    /// <summary>
    /// The fixture validates shaders against the no-GPU engine setting. Compilation must be
    /// real; a shader-cache hit would defeat the purpose.
    /// </summary>
    public GameEngineSetting Setting = GameEngineSetting.CreateNoGPU();

    /// <summary>A game engine subclass used to host shader validation.</summary>
    public class ShaderValidator : GameEngine
    {
        /// <summary>Creates the validator with the given engine setting.</summary>
        /// <param name="setting">Engine setting to use.</param>
        public ShaderValidator(GameEngineSetting setting) : base(setting)
        {
        }
    }

    /// <summary>Tests that all shipped Slang files compile.</summary>
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
