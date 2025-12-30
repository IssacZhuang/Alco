namespace Alco.Audio;

internal static class CheckSumUtility
{
    private static readonly ushort[] Crc8Table = new ushort[256];


    static CheckSumUtility()
    {
        int polySumm = 0x07;
        int bitmask = 0x00FF;

        int poly = (ushort)(polySumm + (1 << 8));
        for (int i = 0; i < Crc8Table.Length; i++)
        {
            int crc = i;
            for (int n = 0; n < 8; n++)
            {
                if ((crc & (1 << (8 - 1))) != 0)
                {
                    crc = ((crc << 1)
                           ^ poly);
                }
                else
                    crc = crc << 1;
            }
            Crc8Table[i] = (ushort)(crc & bitmask);
        }
    }

    public static unsafe byte CalcCheckSum(byte* buffer, int offset, int count)
    {
        int res = 0;
        for (int i = offset; i < offset + count; i++)
        {
            res = Crc8Table[res ^ buffer[i]];
        }
        return (byte)res;
    }
}