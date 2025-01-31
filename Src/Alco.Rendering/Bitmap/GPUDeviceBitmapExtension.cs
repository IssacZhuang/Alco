using Alco.Graphics;

namespace Alco.Rendering;

public unsafe static class GPUDeviceBitmapExtension
{
    public static void WriteTexture<T>(this GPUDevice device, GPUTexture texture, Bitmap<T> bitmap) where T : unmanaged
    {
        var size = bitmap.Width * bitmap.Height * sizeof(T);
        byte* ptr = (byte*)bitmap.UnsafePointer;
        device.WriteTexture(texture, ptr, (uint)size);
    }
}

