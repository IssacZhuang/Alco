using Alco.Engine;
using Alco.Graphics;

namespace Alco.Engine.Test;

/// <summary>
/// Helpers for building <see cref="GameEngineSetting"/> instances in engine unit tests.
/// </summary>
public static class TestEngineSettings
{
    /// <summary>
    /// Returns a NoGPU setting with the on-disk shader cache enabled to speed up engine unit tests.
    /// </summary>
    /// <remarks>
    /// The engine default keeps the shader cache disabled; tests opt in here.
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
