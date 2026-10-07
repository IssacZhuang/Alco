using System.Threading;
using Alco.Graphics;
using NUnit.Framework;
using SDL3;

using static SDL3.SDL3;

namespace Alco.Engine.Test;

/// <summary>
/// Verifies that actual SDL window closure releases its engine-owned surface resources.
/// </summary>
[TestFixture]
[NonParallelizable]
[Category("AlcoGpu")]
[Platform("Win")]
[Apartment(ApartmentState.STA)]
public sealed class TestSdl3WindowLifecycle
{
    private sealed class Host : IGPUDeviceHost, IDisposable
    {
        /// <inheritdoc />
        public event Action? OnEndFrame;
        /// <inheritdoc />
        public event Action? OnDispose;
        /// <summary>Gets errors reported by the graphics device.</summary>
        public List<string> Errors { get; } = new();
        /// <summary>Advances the device's deferred disposal queue.</summary>
        public void EndFrame() => OnEndFrame?.Invoke();
        /// <inheritdoc />
        public void Dispose() => OnDispose?.Invoke();
        /// <inheritdoc />
        public void LogInfo(ReadOnlySpan<char> message) { }
        /// <inheritdoc />
        public void LogWarning(ReadOnlySpan<char> message) => TestContext.Progress.WriteLine(message.ToString());
        /// <inheritdoc />
        public void LogError(ReadOnlySpan<char> message) => Errors.Add(message.ToString());
        /// <inheritdoc />
        public void LogSuccess(ReadOnlySpan<char> message) { }
    }

    /// <summary>
    /// Closes an unpresented acquired frame without draining deferred disposal, then renders
    /// through a second SDL window on the same live device.
    /// </summary>
    /// <param name="backend">The graphics backend used for the real SDL surfaces.</param>
    [TestCase(GraphicsBackend.Auto)]
    [TestCase(GraphicsBackend.WGPUDx12)]
    public void CloseUnpresentedWindowReleasesOwnedResourcesAndKeepsDeviceUsable(GraphicsBackend backend)
    {
        SDL_SetMainReady();
        Assert.That((bool)SDL_Init(SDL_InitFlags.Video), Is.True, SDL_GetError());
        try
        {
            using var host = new Host();
            GPUDevice device = GraphicsDeviceFactory.CreateAlcoGpuDevice(
                new DeviceDescriptor(host, backend, debug: true, disposeDelay: 4));
            using var platform = new Sdl3Platform();
            var first = (Sdl3Window)platform.CreateView(device, new ViewSetting(128, 96, "SDL close regression"));
            SDL_WindowID firstId = SDL_GetWindowID(first.NativeWindow);
            GPUSwapchain firstSwapchain = first.Swapchain!;
            // Refresh once to exercise retirement of the constructor's initial acquired frame.
            Assert.That(firstSwapchain.RequestSurfaceTexture(), Is.True);
            GPUFrameBuffer frameBuffer = firstSwapchain.FrameBuffer;
            GPUTexture texture = frameBuffer.Colors[0];
            GPUTextureView view = frameBuffer.ColorViews[0];
            GPUAttachmentLayout layout = frameBuffer.AttachmentLayout;

            platform.CloseView(first);

            // These are borrowed engine-owned objects, not independently created surface views.
            Assert.That(first.IsDisposed, Is.True);
            Assert.That(firstSwapchain.IsDisposed, Is.True);
            Assert.That(frameBuffer.IsDisposed, Is.True);
            Assert.That(texture.IsDisposed, Is.True);
            Assert.That(view.IsDisposed, Is.True);
            Assert.That(layout.IsDisposed, Is.True);
            Assert.That(SDL_GetWindowFromID(firstId).IsNull, Is.True);
            Assert.DoesNotThrow(first.Dispose);

            var second = (Sdl3Window)platform.CreateView(device, new ViewSetting(128, 96, "SDL replacement regression"));
            SDL_WindowID secondId = SDL_GetWindowID(second.NativeWindow);
            GPUSwapchain secondSwapchain = second.Swapchain!;
            Assert.That(secondSwapchain.RequestSurfaceTexture(), Is.True);
            GPUCommandBuffer commands = device.CreateCommandBuffer();
            try
            {
                commands.Begin();
                using (var pass = commands.BeginRender(secondSwapchain.FrameBuffer))
                {
                    // An empty render pass still exercises the new surface attachment and submission.
                }
                commands.End();
                device.Submit(commands);
                secondSwapchain.Present();
            }
            finally
            {
                device.DestroyImmediate(commands);
            }
            platform.CloseView(second);
            Assert.That(secondSwapchain.IsDisposed, Is.True);
            Assert.That(SDL_GetWindowFromID(secondId).IsNull, Is.True);

            for (int i = 0; i < 6; i++)
            {
                host.EndFrame();
            }
            Assert.That(host.Errors, Is.Empty);
        }
        finally
        {
            SDL_Quit();
        }
    }
}
