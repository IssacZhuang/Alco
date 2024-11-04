namespace Vocore.Graphics;

public struct PushConstantsRange
{
    public PushConstantsRange(ShaderStage stage, uint start, uint end)
    {
        Stage = stage;
        Start = start;
        End = end;
    }
    public ShaderStage Stage;
    public uint Start { get; init; }
    public uint End { get; init; }

    public override string ToString()
    {
        return $"Stage: {Stage}, Start: {Start}, End: {End}";
    }
}