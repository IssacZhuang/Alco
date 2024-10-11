using System.Runtime.InteropServices;

namespace Vocore.Audio;

[StructLayout(LayoutKind.Explicit)]
public unsafe struct VorbisHeader
{
    [FieldOffset(0)]
    public uint CapturePattern;
    [FieldOffset(4)]
    public byte Version;
    [FieldOffset(5)]
    public VorbisHeaderType HeaderType;
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