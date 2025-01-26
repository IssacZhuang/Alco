namespace Alco.Audio;

internal enum WaveFormat : ushort
{
    /// <summary>
    /// PCM format
    /// </summary>
    PCM = 1,
    /// <summary>
    /// IEEE float format
    /// </summary>
    IEEEFloat = 3,
    /// <summary>
    /// ALaw format
    /// </summary>
    ALaw = 6,
    /// <summary>
    /// MuLaw format
    /// </summary>
    MuLaw = 7,
    /// <summary>
    /// Extensible format
    /// </summary>
    Extensible = 0xFFFE
}