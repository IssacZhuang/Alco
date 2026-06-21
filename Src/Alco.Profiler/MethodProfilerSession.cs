namespace Alco.Profiler;

/// <summary>
/// Owns exclusive collection access to the process-wide method profiler.
/// </summary>
public sealed class MethodProfilerSession : IDisposable
{
    private Action<long>? _release;

    internal MethodProfilerSession(Action<long> release, long generation, string ownerName)
    {
        _release = release;
        Generation = generation;
        OwnerName = ownerName;
    }

    /// <summary>
    /// Gets this session's unique generation.
    /// </summary>
    public long Generation { get; }

    /// <summary>
    /// Gets the diagnostic owner name.
    /// </summary>
    public string OwnerName { get; }

    /// <summary>
    /// Releases this session if it is still the active owner.
    /// </summary>
    public void Dispose()
    {
        Action<long>? release = Interlocked.Exchange(ref _release, null);
        release?.Invoke(Generation);
    }
}
