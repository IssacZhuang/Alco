using System.Runtime.CompilerServices;

namespace Vocore.Graphics;

internal static class UtilsBuffer
{
    public unsafe static uint GetStructBufferSize<T>() where T : unmanaged
    {
        return GetBufferSize(sizeof(T));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint GetBufferSize(ulong size)
    {
        return GetBufferSize((uint)size);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint GetBufferSize(int size)
    {
        return GetBufferSize((uint)size);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint GetBufferSize(uint size)
    {
        uint sizeInBytes = (uint)size;
        uint remainder = sizeInBytes % 16;
        //Uniform buffer size must be a multiple of 16 bytes.
        sizeInBytes += remainder == 0 ? 0 : 16 - remainder;
        return sizeInBytes;
    }
}