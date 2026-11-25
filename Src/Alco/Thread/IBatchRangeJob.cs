namespace Alco;

/// <summary>
/// Defines a batch of independent indexed operations executed over contiguous index ranges.
/// </summary>
public interface IBatchRangeJob
{
    /// <summary>Gets the total number of indices to execute, starting at zero.</summary>
    int Count { get; }

    /// <summary>Executes the contiguous range starting at <paramref name="start"/> for <paramref name="count"/> items.</summary>
    /// <param name="start">The zero-based starting index of the range.</param>
    /// <param name="count">The number of items in the range.</param>
    void Execute(int start, int count);
}
