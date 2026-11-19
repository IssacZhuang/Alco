namespace Alco;

/// <summary>
/// Defines a single job to execute on a worker.
/// </summary>
public interface IJob
{
    /// <summary>Executes the job.</summary>
    void Execute();
}
