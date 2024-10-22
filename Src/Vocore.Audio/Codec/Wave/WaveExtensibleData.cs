using System.Drawing;
using System.Runtime.InteropServices;

namespace Vocore.Audio;

[StructLayout(LayoutKind.Explicit)]
internal unsafe struct WaveExtensionData
{
    [FieldOffset(0)]
    public ushort Size;
    [FieldOffset(2)]
    public ushort ValidBitsPerSample;
    [FieldOffset(4)]
    public uint ChannelMask;
    [FieldOffset(8)]
    public unsafe fixed byte SubFormat[16];//GUID, first 2 bytes are the WaveFormat

    public WaveFormat GetWaveFormat()
    {
        fixed (byte* ptr = SubFormat)
        {
            return (WaveFormat)(*(ushort*)ptr);
        }
    }
}