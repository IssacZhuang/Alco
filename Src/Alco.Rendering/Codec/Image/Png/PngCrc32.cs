using System.Buffers.Binary;

namespace Alco.Rendering;

/// <summary>Computes the PNG CRC-32 (IEEE polynomial) using a slicing-by-eight lookup table.</summary>
internal static class PngCrc32
{
    private const int SliceSize = 256;
    private const int Slices = 8;
    private static readonly uint[] s_table = BuildTable();

    private static uint[] BuildTable()
    {
        uint[] table = new uint[Slices * SliceSize];
        for (int n = 0; n < SliceSize; n++)
        {
            uint c = (uint)n;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        for (int slice = 1; slice < Slices; slice++)
        {
            for (int n = 0; n < SliceSize; n++)
            {
                uint c = table[(slice - 1) * SliceSize + n];
                table[slice * SliceSize + n] = table[(byte)c] ^ (c >> 8);
            }
        }

        return table;
    }

    /// <summary>Computes the CRC of the chunk type and payload, excluding the length and CRC fields.</summary>
    public static uint Compute(ReadOnlySpan<byte> data)
    {
        uint crc = uint.MaxValue;
        while (data.Length >= 8)
        {
            uint lo = BinaryPrimitives.ReadUInt32LittleEndian(data) ^ crc;
            uint hi = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
            crc = s_table[7 * SliceSize + (byte)lo]
                ^ s_table[6 * SliceSize + (byte)(lo >> 8)]
                ^ s_table[5 * SliceSize + (byte)(lo >> 16)]
                ^ s_table[4 * SliceSize + (lo >> 24)]
                ^ s_table[3 * SliceSize + (byte)hi]
                ^ s_table[2 * SliceSize + (byte)(hi >> 8)]
                ^ s_table[SliceSize + (byte)(hi >> 16)]
                ^ s_table[hi >> 24];
            data = data[8..];
        }

        for (int i = 0; i < data.Length; i++)
        {
            crc = s_table[(byte)(crc ^ data[i])] ^ (crc >> 8);
        }

        return ~crc;
    }
}
