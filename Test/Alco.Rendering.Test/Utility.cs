using Alco.Graphics;
using Alco.Graphics.NoGPU;
using Alco.Rendering;
using Alco.ShaderCompiler;

namespace Alco.Rendering.Test;

internal static class Utility
{

    internal static DummyRenderingSystemHost CreateRenderingSystem(SlangFileResolver? resolver = null, GPUDevice? device = null)
    {
        GPUDevice gpuDevice = device ?? GraphicsDeviceFactory.GetNoGPUDevice();
        DummyRenderingSystemHost host = new DummyRenderingSystemHost();
        // Tests register module sources explicitly (GetShaderFromModule); a
        // resolver is only passed when a test serves importable files.
        RenderingSystem renderingSystem = new RenderingSystem(
            host,
            gpuDevice,
            PixelFormat.RGBA16Float,
            PixelFormat.Depth24PlusStencil8,
            resolver
        );
        host.RenderingSystem = renderingSystem;
        return host;
    }
}

/// <summary>
/// A NoGPU device that reports BC texture compression support, so tests can
/// exercise the block-compressed creation and upload paths (writes are still
/// discarded like on the plain NoDevice).
/// </summary>
internal sealed class BcCapableNoDevice : NoDevice
{
    public override GPUFeatures SupportedFeatures => GPUFeatures.TextureCompressionBC;
}




