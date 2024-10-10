using System.Runtime.InteropServices;

namespace Vocore.Audio;


[StructLayout(LayoutKind.Explicit)]
public unsafe struct WaveChunckRiff
{
    /// <summary>
    /// "RIFF" text in ASCII
    /// </summary>
    [FieldOffset(0)]
    public fixed byte Riff[4];

    /// <summary>
    /// int32 of the file size
    /// </summary>
    [FieldOffset(4)]
    public uint FileSize;

    /// <summary>
    /// "WAVE" text in ASCII
    /// </summary>
    [FieldOffset(8)]
    public fixed byte Wave[4];
}