namespace SlangSharp;

public readonly struct SlangResult
{
    public static readonly SlangResult Ok = new SlangResult(0);

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
    public uint data1;
    public ushort data2;
    public ushort data3;
    public fixed byte data4[8];
}




