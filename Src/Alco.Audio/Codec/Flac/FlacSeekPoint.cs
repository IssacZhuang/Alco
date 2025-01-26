using System.Runtime.InteropServices;

namespace Alco.Audio;

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct FlacSeekPoint
{
    public ulong SampleNumber;
    public ulong Offset;
    public ushort NumberOfSamples;
}