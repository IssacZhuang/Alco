namespace Alco.Rendering;

/// <summary>
/// CRC-32 computation using the PNG-standard polynomial (ISO 3309 / ITU-T V.42).
/// Uses a precomputed 256-entry lookup table for byte-at-a-time updates.
/// Polynomial: 0xEDB88320 (bit-reversed form of 0x04C11DB7).
/// </summary>
internal static class PngCrc32
{
    private static readonly uint[] s_table = BuildTable();

    private static uint[] BuildTable()
    {
        var table = new uint[256];

        for (int n = 0; n < 256; n++)
        {
            uint c = (uint)n;
            for (int k = 0; k < 8; k++)
            {
                if ((c & 1) != 0)
                    c = 0xEDB88320u ^ (c >> 1);
                else
                    c >>= 1;
            }
            table[n] = c;
        }

        return table;
    }

    /// <summary>
    /// Compute the CRC-32 of the given data.
    /// Equivalent to: initialize 0xFFFFFFFF, update each byte, XOR with 0xFFFFFFFF.
    /// </summary>
    public static uint Compute(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFFu;

        for (int i = 0; i < data.Length; i++)
            crc = s_table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);

        return crc ^ 0xFFFFFFFFu;
    }

    /// <summary>
    /// Compute the CRC-32 of two contiguous regions (e.g. chunk type + chunk data).
    /// </summary>
    public static uint Compute(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        uint crc = 0xFFFFFFFFu;

        for (int i = 0; i < a.Length; i++)
            crc = s_table[(crc ^ a[i]) & 0xFF] ^ (crc >> 8);

        for (int i = 0; i < b.Length; i++)
            crc = s_table[(crc ^ b[i]) & 0xFF] ^ (crc >> 8);

        return crc ^ 0xFFFFFFFFu;
    }
}
