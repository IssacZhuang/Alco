using System.Runtime.InteropServices;

namespace Vocore.Audio;

[StructLayout(LayoutKind.Explicit)]
public unsafe struct VorbisHeader
{
    [FieldOffset(0)]
    public VorbisPacketType Type;
    [FieldOffset(1)]
    public fixed byte Vendor[6];//always "vorbis"
    [FieldOffset(7)]
    public uint Version;
    [FieldOffset(11)]
    public byte Channels;
    [FieldOffset(12)]
    public uint SampleRate;
    [FieldOffset(16)]
    public uint BitrateMax;
    [FieldOffset(20)]
    public uint BitrateNominal;
    [FieldOffset(24)]
    public uint BitrateMin;
    [FieldOffset(28)]
    public byte BlockSize0;
    [FieldOffset(29)]
    public byte BlockSize1;
    [FieldOffset(30)]
    public byte FramingFlag;
}