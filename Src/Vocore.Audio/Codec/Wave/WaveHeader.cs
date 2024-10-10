using System.Runtime.InteropServices;

namespace Vocore.Audio;

[StructLayout(LayoutKind.Explicit)]
public unsafe struct WaveHeader
{
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

    /// <summary>
    /// "fmt " text in ASCII
    /// </summary>
    /// <remarks>Notice the space at the end</remarks>
   [FieldOffset(12)]
    public fixed byte Fmt[4];

    /// <summary>
    /// The size of the fmt chunk
    /// </summary>
    [FieldOffset(16)]
    public uint FmtSize;

    /// <summary>
    /// The audio format, 1 for PCM
    /// </summary>
    [FieldOffset(20)]
    public WaveFormat WaveFormat;

    /// <summary>
    /// The number of channels
    /// </summary>
    [FieldOffset(22)]
    public ushort Channels;

    /// <summary>
    /// The sample rate
    /// </summary>
    [FieldOffset(24)]
    public uint SampleRate;

    /// <summary>
    /// SampleRate * Channels * BitsPerSample / 8
    /// </summary>
    [FieldOffset(28)]
    public uint ByteRate;

    /// <summary>
    /// Channels * BitsPerSample / 8
    /// </summary>
    [FieldOffset(32)]
    public ushort BlockAlign;

    /// <summary>
    /// The bits per sample
    /// </summary>
    [FieldOffset(34)]
    public ushort BitsPerSample;

    /// <summary>
    /// "data" text in ASCII
    /// </summary>
    [FieldOffset(36)]
    public fixed byte Data[4];

    /// <summary>
    /// The size in bytes of the data chunk
    /// </summary>
    [FieldOffset(40)]
    public uint DataSize;

    public override string ToString()
    {
        return $"Channels: {Channels}, SampleRate: {SampleRate}, BitsPerSample: {BitsPerSample}, DataSize: {DataSize}";
    }
}