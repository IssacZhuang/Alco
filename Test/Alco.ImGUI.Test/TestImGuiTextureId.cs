using Alco.Graphics;
using Alco.Graphics.LowLevel;
using Alco.Rendering;

namespace Alco.ImGUI.Test;

/// <summary>
/// Verifies stable bindless <see cref="ImGui.GetTextureId(Texture?)"/> resolution and fallback behavior.
/// </summary>
[TestFixture]
public sealed class TestImGuiTextureId
{
    /// <summary>
    /// Verifies that null, non-sampled, destroyed, and valid textures resolve to expected ImGui texture IDs.
    /// </summary>
    [Test]
    public void GetTextureId_ResolvesSampledSlotAndFallsBackToWhiteSlotZero()
    {
        GPUDevice device = Gpu.CreateDevice(new DeviceDesc { Backend = GraphicsBackend.NoGPU });
        RenderingSystem system = new(device, textureHeapByteCount: 1024 * 1024, bufferHeapByteCount: 1024 * 1024);
        Texture? sampled = null;
        Texture? attachmentOnly = null;
        Texture? utilityTex = null;
        try
        {
            Assert.That(system.TextureWhite.SampledIndex, Is.EqualTo(0u));
            Assert.That(ImGui.GetTextureId((Texture?)null), Is.EqualTo(IntPtr.Zero));

            sampled = system.CreateTexture(new TextureDesc
            {
                Extent = new Extent3D(8, 8, 1),
                Format = Format.Rgba8Unorm,
                Usage = TextureUsage.Sampled,
            });
            Assert.That(sampled.SampledIndex, Is.Not.EqualTo(0u));
            Assert.That(ImGui.GetTextureId(sampled), Is.EqualTo((IntPtr)sampled.SampledIndex));

            sampled.Dispose();
            Assert.That(ImGui.GetTextureId(sampled), Is.EqualTo(IntPtr.Zero));

            attachmentOnly = system.CreateTexture(new TextureDesc
            {
                Extent = new Extent3D(8, 8, 1),
                Format = Format.Rgba8Unorm,
                Usage = TextureUsage.ColorAttachment,
            });
            Assert.That(attachmentOnly.SampledIndex, Is.EqualTo(Texture.NoIndex));
            Assert.That(ImGui.GetTextureId(attachmentOnly), Is.EqualTo(IntPtr.Zero));

            byte[] pixels = new byte[4 * 4 * 4];
            utilityTex = TextureUtility.CreateTexture(system, pixels, 4, 4);
            Assert.That(utilityTex.SampledIndex, Is.Not.EqualTo(0u));
            Assert.That(ImGui.GetTextureId(utilityTex), Is.EqualTo((IntPtr)utilityTex.SampledIndex));

            utilityTex.Dispose();
            Assert.That(ImGui.GetTextureId(utilityTex), Is.EqualTo(IntPtr.Zero));
        }
        finally
        {
            if (utilityTex != null && !utilityTex.IsDisposed)
            {
                utilityTex.Dispose();
            }
            if (attachmentOnly != null && !attachmentOnly.IsDisposed)
            {
                attachmentOnly.Dispose();
            }
            if (sampled != null && !sampled.IsDisposed)
            {
                sampled.Dispose();
            }
            system.Dispose();
            device.Destroy();
        }
    }
}
