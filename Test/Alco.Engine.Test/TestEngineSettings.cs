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
    /// tests don't recompile every Slang module on every fresh engine instance.
    /// The first run compiles and writes serialized module IR plus linked programs;
    /// subsequent runs restore the cached program and reflection. The cache lives
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
