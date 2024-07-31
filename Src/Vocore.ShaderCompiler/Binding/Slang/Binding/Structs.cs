namespace SlangSharp;

public readonly struct SlangResult
{
    public static readonly SlangResult Ok = new SlangResult(0);
    public static readonly SlangResult Failed = new SlangResult(-1);

    public readonly int Code;

    public SlangResult(int code)
    {
        this.Code = code;
    }

    public bool IsOk => Code >= 0;
    public bool IsError => Code < 0;

    public static implicit operator SlangResult(int code) => new SlangResult(code);
    public static implicit operator int(SlangResult result) => result.Code;


    public override string ToString()
    {
        return $"Slang result code: {Code}";
    }

}

// struct SlangUUID
// {
//     uint32_t data1;
//     uint16_t data2;
//     uint16_t data3;
//     uint8_t data4[8];
// };

public unsafe struct SlangUUID
{
    public SlangUUID(
        uint a, ushort b, ushort c,
        byte d0, byte d1, byte d2, byte d3, byte d4, byte d5, byte d6, byte d7)
    {
        data1 = a;
        data2 = b;
        data3 = c;

        data4[0] = d0;
        data4[1] = d1;
        data4[2] = d2;
        data4[3] = d3;
        data4[4] = d4;
        data4[5] = d5;
        data4[6] = d6;
        data4[7] = d7;
    }

    //operator ==
    public static bool operator ==(SlangUUID left, SlangUUID right)
    {
        return left.data1 == right.data1
            && left.data2 == right.data2
            && left.data3 == right.data3
            && left.data4[0] == right.data4[0]
            && left.data4[1] == right.data4[1]
            && left.data4[2] == right.data4[2]
            && left.data4[3] == right.data4[3]
            && left.data4[4] == right.data4[4]
            && left.data4[5] == right.data4[5]
            && left.data4[6] == right.data4[6]
            && left.data4[7] == right.data4[7];
    }

    //operator !=
    public static bool operator !=(SlangUUID left, SlangUUID right)
    {
        return !(left == right);
    }

    public uint data1;
    public ushort data2;
    public ushort data3;
    public fixed byte data4[8];

    public override bool Equals(object? obj)
    {
        return obj is SlangUUID u && this == u;
    }

    public override int GetHashCode()
    {
        return data1.GetHashCode() ^ data2.GetHashCode() ^ data3.GetHashCode() ^ data4[0].GetHashCode() ^ data4[1].GetHashCode() ^ data4[2].GetHashCode() ^ data4[3].GetHashCode() ^ data4[4].GetHashCode() ^ data4[5].GetHashCode() ^ data4[6].GetHashCode() ^ data4[7].GetHashCode();
    }
}




