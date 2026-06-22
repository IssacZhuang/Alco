namespace Alco.Profiler.BuildTool.Fixture;

/// <summary>
/// Supplies representative IL shapes for build-tool integration tests.
/// </summary>
public static class FixtureMethods
{
    /// <summary>
    /// Adds two values and exercises an early return.
    /// </summary>
    /// <param name="left">Left value.</param>
    /// <param name="right">Right value.</param>
    /// <returns>The sum, or zero when both inputs are zero.</returns>
    public static int Add(int left, int right)
    {
        if (left == 0 && right == 0)
        {
            return 0;
        }
        return left + right;
    }

    /// <summary>
    /// Throws a deterministic exception.
    /// </summary>
    public static void Throw()
    {
        throw new InvalidOperationException("fixture");
    }

    /// <summary>
    /// Exercises a value-preserving branch that targets a shared return instruction.
    /// </summary>
    /// <param name="value">Optional value.</param>
    /// <returns>The value or an empty string.</returns>
    public static string CoalescedReturn(string? value)
    {
        return value ?? string.Empty;
    }

    /// <summary>
    /// Exercises async state-machine mapping and suspension.
    /// </summary>
    /// <param name="value">Value returned after yielding.</param>
    /// <returns>The supplied value.</returns>
    public static async Task<int> YieldAsync(int value)
    {
        await Task.Yield();
        return value;
    }

    /// <summary>
    /// Exercises iterator state-machine mapping and repeated execution intervals.
    /// </summary>
    /// <param name="count">Number of values to yield.</param>
    /// <returns>Values from zero to count minus one.</returns>
    public static IEnumerable<int> YieldValues(int count)
    {
        for (int i = 0; i < count; i++)
        {
            yield return i;
        }
    }
}
