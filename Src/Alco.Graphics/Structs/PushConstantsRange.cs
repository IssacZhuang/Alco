namespace Alco.Graphics;

/// <summary>
/// A byte range of the push constants (immediates) block used by a shader.
/// </summary>
public struct PushConstantsRange
{
    /// <summary>Initializes the range with its start and end byte offsets.</summary>
    /// <param name="start">The start offset of the range in bytes.</param>
    /// <param name="end">The end offset of the range in bytes.</param>
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

    /// <summary>Returns a diagnostic description of the range.</summary>
    public override string ToString()
    {
        return $"Start: {Start}, End: {End}";
    }
}
