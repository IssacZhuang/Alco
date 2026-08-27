using Alco.Graphics;
using Alco.IO;
using Alco.Rendering;
using Alco.ShaderCompiler;

namespace Alco.World3D.Test;

internal static class Utility
{

    internal static DummyRenderingSystemHost CreateRenderingSystem(SlangFileResolver? resolver = null)
    {
        GPUDevice device = GraphicsDeviceFactory.GetNoGPUDevice();
        DummyRenderingSystemHost host = new DummyRenderingSystemHost();
        RenderingSystem renderingSystem = new RenderingSystem(
            host,
            device,
            PixelFormat.RGBA16Float,
            PixelFormat.Depth24PlusStencil8,
            resolver
        );
        host.RenderingSystem = renderingSystem;
        return host;
    }
}

/// <summary>
/// Minimal asset system host for loader tests: logs are dropped, main-thread posts run inline.
/// Dispose it to dispose the asset system (via the constructor subscription).
/// </summary>
internal sealed class TestAssetHost : IAssetSystemHost, IDisposable
{
    public TestAssetHost()
    {
        // Log methods are no-ops by design and nothing here listens for dispose,
        // but the interface-required event must still be initialized.
        OnDispose += () => { };
    }

    public event Action OnDispose;

    public void PostToMainThread(Action action) => action();

    public void LogInfo(ReadOnlySpan<char> message) { }

    public void LogWarning(ReadOnlySpan<char> message) { }

    public void LogError(ReadOnlySpan<char> message) { }

    public void LogSuccess(ReadOnlySpan<char> message) { }

    public void Dispose() => OnDispose?.Invoke();
}
