using System.Numerics;
using Alco.Graphics;
using Alco.Graphics.LowLevel;
using Alco.Rendering;

namespace Alco.GUI.Test;

internal sealed unsafe class UITestHost : IDisposable
{
    public GPUDevice Device { get; }
    public RenderingSystem RenderingSystem { get; }
    public RenderContext Context { get; }

    public UITestHost(GraphicsBackend backend = GraphicsBackend.NoGPU)
    {
        Device = Gpu.CreateDevice(new DeviceDesc { Backend = backend, Headless = true });
        RenderingSystem = new RenderingSystem(Device, textureHeapByteCount: 16 * 1024 * 1024, bufferHeapByteCount: 16 * 1024 * 1024);
        Context = new RenderContext(RenderingSystem);
    }

    public static byte[] LoadDefaultFontBytes()
    {
        string path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "Fonts", "Default.ttf");
        return File.ReadAllBytes(path);
    }

    public Texture2D CreateRgba8Texture(int width, int height, ReadOnlySpan<byte> rgba, string name = "test_tex")
    {
        return Texture2DUtility.CreateTexture2D(
            RenderingSystem,
            rgba,
            (uint)width,
            (uint)height,
            ImageLoadOption.Default with { Format = Format.Rgba8Unorm, Name = name });
    }

    public Texture CreateColorTarget(uint width, uint height)
    {
        return RenderingSystem.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D(width, height, 1),
            Format = Format.Rgba8Unorm,
            Usage = TextureUsage.ColorAttachment | TextureUsage.Sampled | TextureUsage.TransferSource,
        });
    }

    public Texture CreateStencilTarget(uint width, uint height)
    {
        return RenderingSystem.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D(width, height, 1),
            Format = Format.S8Uint,
            Usage = TextureUsage.DepthStencilAttachment,
        });
    }

    public byte[] ReadbackRgba8(Texture target, uint width, uint height)
    {
        ulong byteCount = (ulong)width * height * 4;
        GPUHeap readback = Device.AllocateGPUHeap(byteCount, MemoryType.Readback);
        try
        {
            // Separate readback submission ensures consistent host visibility across Vulkan drivers.
            Context.BeginFrame();
            Context.CopyTextureToMemory(target.NativeTexture, readback.Range.GPURange);
            Context.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
            Context.Submit().Wait();

            byte[] pixels = new byte[byteCount];
            new ReadOnlySpan<byte>(readback.Range.Cpu, (int)byteCount).CopyTo(pixels);
            return pixels;
        }
        finally
        {
            readback.Destroy();
        }
    }

    public void Dispose()
    {
        Device.WaitIdle();
        if (!Context.IsDestroyed)
            Context.Destroy();
        RenderingSystem.Destroy();
        Device.Destroy();
    }
}

internal sealed class TestInputTracker : IUIInputTracker
{
    public Vector2 CursorPosition { get; set; }
    public Vector2 WindowSize { get; set; } = new(128f, 128f);
    public bool IsMouseLeftPressing { get; set; }
    public bool IsConfirmPressing { get; set; }
    public bool IsKeyDeletePressing { get; set; }
    public bool IsKeyBackspacePressing { get; set; }
    public bool IsKeyEnterPressing { get; set; }
    public bool IsKeyTabPressing { get; set; }
    public bool IsKeyLeftPressing { get; set; }
    public bool IsKeyRightPressing { get; set; }
    public bool IsKeyUpPressing { get; set; }
    public bool IsKeyDownPressing { get; set; }
    public bool IsKeySelectAllPressing { get; set; }
    public bool IsKeyCopyPressing { get; set; }
    public bool IsKeyPastePressing { get; set; }
    public bool IsGamepadInputting { get; set; }

    public bool IsScrolling(out Vector2 delta)
    {
        delta = Vector2.Zero;
        return false;
    }

    public void SetTextInput(float xNorm, float yNorm, float widthNorm, float heightNorm, int cursor) { }
    public void CopyToClipboard(ReadOnlySpan<char> text) { }
    public ReadOnlySpan<char> GetClipboardText() => default;
    public void RegisterTextInput(Action<ReadOnlySpan<char>> action) { }
    public void UnregisterTextInput(Action<ReadOnlySpan<char>> action) { }
    public void RequestTextInput() { }
    public void ReleaseTextInput() { }
}
