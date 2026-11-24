using Alco.Graphics;
using NUnit.Framework;

namespace Alco.Engine.Test;

/// <summary>
/// Verifies <see cref="GameEngine"/> wires <see cref="AssetSystem"/> module resolution and shader cache
/// configuration directly into <see cref="RenderingSystem.Shaders"/>.
/// </summary>
[TestFixture]
public class TestEngineShaderSystem
{
    private const string VirtualAssetShaderSource = """
        import Core;

        [GPURoot]
        struct EngineTestRoot
        {
            GPUBufferView Output;
            float4 Value;
        };

        [shader("compute")]
        [numthreads(1, 1, 1)]
        void csMain(uint3 id : SV_DispatchThreadID)
        {
            EngineTestRoot root = gpu_load_root<EngineTestRoot>();
            gpu_store(root.Output, id.x, root.Value);
        }
        """;

    /// <summary>Verifies <see cref="RenderingSystem.Shaders"/> resolves asset shaders, writes disk cache, and disposes with the engine.</summary>
    [Test]
    public void RenderingSystemShaders_UsesEngineAssetResolverAndCache_AndDisposesWithEngine()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"alco_engine_shaders_{Guid.NewGuid():N}");
        string assetsDir = Path.Combine(tempRoot, "Assets");
        string shadersDir = Path.Combine(assetsDir, "Shaders");
        string cacheDir = Path.Combine(tempRoot, "ShaderCache");
        Directory.CreateDirectory(shadersDir);
        File.WriteAllText(Path.Combine(shadersDir, "EngineResolvedCompute.slang"), VirtualAssetShaderSource);

        try
        {
            GameEngineSetting setting = GameEngineSetting.CreateNoGPU();
            setting.Assets = setting.Assets with { AssetsPath = assetsDir };
            setting.Graphics = setting.Graphics with
            {
                IsShaderCacheEnabled = true,
                ShaderCachePath = cacheDir,
            };

            Shader shader;
            using (GameEngine engine = new(setting))
            {
                shader = engine.RenderingSystem.Shaders.GetShader("EngineResolvedCompute");
                Assert.That(shader.Kind, Is.EqualTo(ShaderKind.Compute));
                Assert.That(shader.IsDestroyed, Is.False);
                Assert.That(Directory.GetFiles(cacheDir, "*.agshader"), Is.Not.Empty);
            }

            Assert.That(shader.IsDestroyed, Is.True);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }
}
