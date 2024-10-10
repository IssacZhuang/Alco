using System.Runtime.InteropServices;

namespace Vocore.Audio;

[StructLayout(LayoutKind.Explicit)]
public unsafe struct WaveChunckData
{
    /// <summary>
    /// "data" text in ASCII
    /// </summary>
    [FieldOffset(0)]
    public fixed byte Data[4];

    /// <summary>
    /// The size in bytes of the data chunk
    /// </summary>
    [FieldOffset(4)]
    public uint DataSize;
}