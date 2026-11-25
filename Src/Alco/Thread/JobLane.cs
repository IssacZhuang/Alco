namespace Alco;

/// <summary>
/// Selects the execution lane and priority tier for a scheduled job.
/// </summary>
public enum JobLane : byte
{
    /// <summary>High-priority lane for frame-critical work completed within the current tick or update.</summary>
    Frame = 0,

    /// <summary>Low-priority lane for cross-frame asynchronous work such as simulation or pathfinding.</summary>
    Background = 1,
}
