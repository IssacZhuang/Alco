using System.Runtime.InteropServices;

namespace Vocore.Audio;


[StructLayout(LayoutKind.Explicit)]
internal unsafe struct WaveChunckUnknown{
    [FieldOffset(0)]
    public fixed byte Name[4];

    [FieldOffset(4)]
    public uint Size;
}