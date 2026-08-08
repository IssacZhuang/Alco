namespace Alco.Graphics;

/// <summary>
/// A byte range of the push constants (immediates) block used by a shader.
/// </summary>
public struct PushConstantsRange
{
    public PushConstantsRange(uint start, uint end)
    {
        Start = start;
        End = end;
    }

    /// <summary>
    /// The start offset of the range in bytes.
    /// </summary>
    public uint Start { get; init; }

    /// <summary>
    /// The end offset of the range in bytes.
    /// </summary>
    public uint End { get; init; }

    public override string ToString()
    {
        return $"Start: {Start}, End: {End}";
    }
}
