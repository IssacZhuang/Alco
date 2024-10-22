using System.Runtime.InteropServices;
using System.Text;

namespace Vocore.Audio;


[StructLayout(LayoutKind.Explicit, Pack = 1)]
internal unsafe struct WaveChunckRiff
{
    public static readonly byte[] ChunckName = Encoding.ASCII.GetBytes("RIFF");
    public static bool IsRiffChunk(byte* ptr)
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
    /// "RIFF" text in ASCII
    /// </summary>
    [FieldOffset(0)]
    public fixed byte Riff[4];

    /// <summary>
    /// int32 of the file size
    /// </summary>
    [FieldOffset(4)]
    public uint FileSize;

    /// <summary>
    /// "WAVE" text in ASCII
    /// </summary>
    [FieldOffset(8)]
    public fixed byte Wave[4];
}