using System.Runtime.InteropServices;

namespace Vocore.Audio;

[StructLayout(LayoutKind.Explicit)]
public unsafe struct OggHeader
{
    [FieldOffset(0)]
    public fixed byte CapturePattern[4];//"OggS" in ASCII
    [FieldOffset(4)]
    public byte Version;
    [FieldOffset(5)]
    public OggPageFlag PageFlag;
    [FieldOffset(6)]
    public ulong GranulePosition;
    [FieldOffset(14)]
    public uint SerialNumber;
    [FieldOffset(18)]
    public uint PageSequenceNumber;
    [FieldOffset(22)]
    public uint Checksum;
    [FieldOffset(26)]
    public byte PageSegments;
}