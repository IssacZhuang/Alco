using Alco.Engine;
using Alco.Graphics;

namespace Alco.Engine.Test;

/// <summary>
/// Helpers for building <see cref="GameEngineSetting"/> instances in engine unit tests.
/// </summary>
public static class TestEngineSettings
{
    /// <summary>
    /// Returns a NoGPU setting with the on-disk shader cache enabled, so engine unit
    /// tests don't recompile every shader (DXC, 100-300ms each) on every fresh engine
    /// instance. The first run compiles and writes SPIR-V + reflection; subsequent runs
    /// decode the cached ShaderModulesInfo (SPIRV-Reflect only, no DXC). The cache lives
    /// under the test output directory (gitignored) and is shared across engine instances.
    /// </summary>
    /// <remarks>
    /// The engine default <see cref="GameEngineSetting.CreateNoGPU"/> keeps the shader
    /// cache disabled; tests opt in here so the engine default stays unchanged.
    /// </remarks>
    public static GameEngineSetting CreateNoGPUWithShaderCache()
    {
        GameEngineSetting setting = GameEngineSetting.CreateNoGPU();
        setting.Graphics = setting.Graphics with
        {
            IsShaderCacheEnabled = true,
            ShaderCachePath = ".cache/shader",
        };
        return setting;
    }
}
