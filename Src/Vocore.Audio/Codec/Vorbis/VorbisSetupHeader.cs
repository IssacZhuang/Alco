using System.Runtime.InteropServices;

namespace Vocore.Audio;

[StructLayout(LayoutKind.Explicit, Pack = 1)]
internal unsafe struct VorbisSetupHeader
{
    [FieldOffset(0)]
    public VorbisHeaderType Type;
    [FieldOffset(1)]
    public fixed byte Vendor[6];//always "vorbis"
    [FieldOffset(7)]
    public byte CodebookCount;

    public override string ToString()
    {
        return $"VorbisHeader\n Type: {Type}\n CodebookCount: {CodebookCount}\n";
    }
}