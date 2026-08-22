namespace Alco.Rendering;

// All target-backend exceptions live here. Slang's direct SPIR-V emitter is the
// default; the renderer-shaped World3D PBR programs use the GLSL -> glslang
// fallback because their nested/dynamic loops can produce NVIDIA Vulkan device
// loss with direct output. This is the same failure class tracked upstream for
// direct SPIR-V expansion: https://github.com/shader-slang/slang/issues/5538
// Re-test this predicate whenever the pinned Slang release changes.
internal static class SpirvCompat
{
    private const string World3DPbrPipeline = "/ShadersSlang/Pipelines/Rendering/PBR/";
    private const string BlueNoiseModule = "ScreenSpaceReflectionBlueNoise.slang";

    public static bool RequiresGlslang(string assetPath)
    {
        string normalized = assetPath.Replace('\\', '/');
        if (!normalized.StartsWith('/'))
        {
            normalized = "/" + normalized;
        }
        if (!normalized.Contains(World3DPbrPipeline, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // This module must remain direct: the GLSL path emits a cast that is
        // valid to glslang but rejected when wgpu/naga imports the SPIR-V.
        // The upstream via-GLSL cast issue class is tracked here:
        // https://github.com/shader-slang/slang/issues/7838
        return !normalized.EndsWith(BlueNoiseModule, StringComparison.OrdinalIgnoreCase);
    }
}
