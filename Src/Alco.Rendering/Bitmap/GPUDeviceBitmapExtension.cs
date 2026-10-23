using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Extensions for uploading bitmap data to GPU textures.
/// </summary>
public unsafe static class GPUDeviceBitmapExtension
{
    /// <summary>
    /// Uploads the bitmap's pixels to the texture.
    /// </summary>
    /// <typeparam name="T">The unmanaged pixel component type.</typeparam>
    /// <param name="device">The GPU device.</param>
    /// <param name="texture">The target texture.</param>
    /// <param name="bitmap">The source bitmap.</param>
    public static void WriteTexture<T>(this GPUDevice device, GPUTexture texture, Bitmap<T> bitmap) where T : unmanaged
    {
        var size = bitmap.Width * bitmap.Height * sizeof(T);
        byte* ptr = (byte*)bitmap.UnsafePointer;
        device.WriteTexture(texture, ptr, (uint)size);
    }
}

