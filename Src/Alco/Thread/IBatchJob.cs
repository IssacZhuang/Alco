namespace Alco;

/// <summary>
/// Defines a batch of independent indexed operations that can run concurrently.
/// </summary>
public interface IBatchJob
{
    /// <summary>Gets the number of indices to execute, starting at zero.</summary>
    int Count { get; }

    /// <summary>Executes one operation in the batch.</summary>
    /// <param name="index">The zero-based index of the operation.</param>
    void Execute(int index);
}
