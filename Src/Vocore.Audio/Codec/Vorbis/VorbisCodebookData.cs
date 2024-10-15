
using System.Runtime.InteropServices;

namespace Vocore.Audio;

[StructLayout(LayoutKind.Explicit, Pack = 1)]
internal unsafe ref struct VorbisCodebookData
{
    [FieldOffset(0)]
    public fixed byte SyncPattern[3];//always "BCV"
    [FieldOffset(3)]
    public ushort CodebookDimensions;
    [FieldOffset(5)]
    public UInt24 CodebookEntries;
    [FieldOffset(8)]
    public byte OrderedFlag;

    public override string ToString()
    {
        return $"VorbisCodebookData\n CodebookDimensions: {CodebookDimensions}\n CodebookEntries: {CodebookEntries}\n OrderedFlag: {OrderedFlag}";
    }
}