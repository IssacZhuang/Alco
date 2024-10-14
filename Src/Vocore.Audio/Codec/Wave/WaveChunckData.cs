using System.Runtime.InteropServices;
using System.Text;

namespace Vocore.Audio;

[StructLayout(LayoutKind.Explicit, Pack = 1)]
public unsafe struct WaveChunckData
{
    public static readonly byte[] ChunckName = Encoding.ASCII.GetBytes("data");
    public static bool IsDataChunk(byte* ptr)
    {
        for (int i = 0; i < 4; i++)
        {
            if (ptr[i] != ChunckName[i])
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// "data" text in ASCII
    /// </summary>
    [FieldOffset(0)]
    public fixed byte Data[4];

    /// <summary>
    /// The size in bytes of the data chunk
    /// </summary>
    [FieldOffset(4)]
    public uint DataSize;
}