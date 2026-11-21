using System.Threading;
using Alco.Graphics;
using Alco.Graphics.LowLevel;
using NUnit.Framework;
using SDL3;

using static SDL3.SDL3;

namespace Alco.Engine.Test;

/// <summary>
/// Verifies that actual SDL window closure releases its engine-owned swapchain and keeps the device usable.
/// </summary>
[TestFixture]
[NonParallelizable]
[Category("AlcoGpu")]
[Platform("Win")]
[Apartment(ApartmentState.STA)]
public sealed class TestSdl3WindowLifecycle
{
    /// <summary>
    /// Closes an unpresented acquired frame, then acquires, renders, and presents through a second SDL window on the same live device.
    /// </summary>
    /// <param name="backend">The graphics backend used for the real SDL surfaces.</param>
    [TestCase(GraphicsBackend.Auto)]
    [TestCase(GraphicsBackend.Dx12)]
    [TestCase(GraphicsBackend.VulkanCompact)]
    public void CloseUnpresentedWindowReleasesOwnedResourcesAndKeepsDeviceUsable(GraphicsBackend backend)
    {
        List<string> errors = [];
        Gpu.SetErrorCallback((message, _) => errors.Add(message));

        SDL_SetMainReady();
        Assert.That((bool)SDL_Init(SDL_InitFlags.Video), Is.True, SDL_GetError());
        GPUDevice? device = null;
        RenderingSystem? rendering = null;
        Sdl3Platform? platform = null;
        try
        {
            device = Gpu.CreateDevice(new DeviceDesc { Backend = backend, Headless = false });
            rendering = new RenderingSystem(device, textureHeapByteCount: 4 * 1024 * 1024, bufferHeapByteCount: 1 * 1024 * 1024);
            platform = new Sdl3Platform();

            var first = (Sdl3Window)platform.CreateView(device, new ViewSetting(128, 96, "SDL close regression"));
            SDL_WindowID firstId = SDL_GetWindowID(first.NativeWindow);
            RenderContext firstContext = new(rendering);
            try
            {
                firstContext.BeginFrame();
                SwapchainFrame firstFrame = firstContext.Acquire(first.Swapchain!);
                Assert.That(firstFrame.RenderView, Is.Not.Null);
                firstContext.DiscardFrame();
            }
            finally
            {
                firstContext.Destroy();
            }

            platform.CloseView(first);

            Assert.That(first.IsDisposed, Is.True);
            Assert.That(SDL_GetWindowFromID(firstId).IsNull, Is.True);
            Assert.DoesNotThrow(first.Dispose);

            var second = (Sdl3Window)platform.CreateView(device, new ViewSetting(128, 96, "SDL replacement regression"));
            SDL_WindowID secondId = SDL_GetWindowID(second.NativeWindow);
            GPUSwapchain secondSwapchain = second.Swapchain!;
            RenderContext secondContext = new(rendering);
            try
            {
                secondContext.BeginFrame();
                SwapchainFrame secondFrame = secondContext.Acquire(secondSwapchain);
                Assert.That(secondFrame.RenderView, Is.Not.Null);

                ColorAttachment color = new()
                {
                    RenderView = secondFrame.RenderView,
                    Load = LoadOp.Clear,
                    Store = StoreOp.Store,
                };
                RenderingDesc pass = new() { Colors = [color] };
                using (secondContext.BeginRenderPass(pass, [secondSwapchain.Format]))
                {
                }

                secondContext.SubmitAndPresent(secondSwapchain).Wait();
            }
            finally
            {
                secondContext.Destroy();
            }

            platform.CloseView(second);
            Assert.That(SDL_GetWindowFromID(secondId).IsNull, Is.True);

            Assert.That(errors, Is.Empty);
        }
        finally
        {
            device?.WaitIdle();
            platform?.Dispose();
            rendering?.Destroy();
            device?.Destroy();
            Gpu.SetErrorCallback(null);
            SDL_Quit();
        }
    }
}
