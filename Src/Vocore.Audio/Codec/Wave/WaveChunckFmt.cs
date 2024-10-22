using System.Runtime.InteropServices;
using System.Text;

namespace Vocore.Audio;


[StructLayout(LayoutKind.Explicit, Pack = 1)]
internal unsafe struct WaveChunckFmt
{
    public static readonly byte[] ChunckName = Encoding.ASCII.GetBytes("fmt ");
    public static bool IsFmtChunk(byte* ptr)
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
    /// "fmt " text in ASCII
    /// </summary>
    /// <remarks>Notice the space at the end</remarks>
    [FieldOffset(0)]
    public fixed byte Fmt[4];

    /// <summary>
    /// The size of the fmt chunk
    /// </summary>
    [FieldOffset(4)]
    public uint FmtSize;

    /// <summary>
    /// The audio format, 1 for PCM
    /// </summary>
    [FieldOffset(8)]
    public WaveFormat WaveFormat;

    /// <summary>
    /// The number of channels
    /// </summary>
    [FieldOffset(10)]
    public ushort Channels;

    /// <summary>
    /// The sample rate
    /// </summary>
    [FieldOffset(12)]
    public uint SampleRate;

    /// <summary>
    /// SampleRate * Channels * BitsPerSample / 8
    /// </summary>
    [FieldOffset(16)]
    public uint ByteRate;

    /// <summary>
    /// Channels * BitsPerSample / 8
    /// </summary>
    [FieldOffset(20)]
    public ushort BlockAlign;

    /// <summary>
    /// The bits per sample
    /// </summary>
    [FieldOffset(22)]
    public ushort BitsPerSample;

    public override string ToString()
    {
        return $"Channels: {Channels}, SampleRate: {SampleRate}, BitsPerSample: {BitsPerSample}";
    }
}