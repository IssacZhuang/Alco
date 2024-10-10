using System.Runtime.InteropServices;

namespace Vocore.Audio;


[StructLayout(LayoutKind.Explicit)]
public unsafe struct WaveChunckFmt
{
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