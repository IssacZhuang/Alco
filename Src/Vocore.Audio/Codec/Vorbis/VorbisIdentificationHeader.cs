using System.Runtime.InteropServices;

namespace Vocore.Audio;

[StructLayout(LayoutKind.Explicit, Pack = 1)]
internal unsafe struct VorbisIdentificationHeader
{
    [FieldOffset(0)]
    public VorbisHeaderType Type;
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

    override public string ToString()
    {
        return $"VorbisHeader\n Type: {Type}\n Version: {Version}\n Channels: {Channels}\n SampleRate: {SampleRate}\n BitrateMax: {BitrateMax}\n BitrateNominal: {BitrateNominal}\n BitrateMin: {BitrateMin}\n BlockSize0: {BlockSize0}\n BlockSize1: {BlockSize1}\n FramingFlag: {FramingFlag}";
    }
}