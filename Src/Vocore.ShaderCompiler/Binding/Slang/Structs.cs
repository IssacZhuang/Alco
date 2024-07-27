namespace SlangSharp;

public readonly struct SlangResult
{
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


