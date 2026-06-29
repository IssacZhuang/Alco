namespace Alco.Profiler;

/// <summary>
/// Contains aggregate timing for one method on one managed thread during a logical tick.
/// </summary>
/// <param name="MethodId">Registered method identifier.</param>
/// <param name="ThreadId">Managed thread identifier.</param>
/// <param name="Inclusive">Total inclusive duration.</param>
/// <param name="Self">Duration excluding tracked child methods.</param>
/// <param name="Calls">Number of measured execution intervals.</param>
/// <param name="Maximum">Longest measured interval.</param>
public readonly record struct MethodProfileSample(
    ulong MethodId,
    int ThreadId,
    TimeSpan Inclusive,
    TimeSpan Self,
    long Calls,
    TimeSpan Maximum);

/// <summary>
/// Contains outermost tagged-scope timing grouped by a captured concrete runtime type.
/// </summary>
/// <param name="Tag">Aggregation channel.</param>
/// <param name="ContextType">Captured concrete runtime type.</param>
/// <param name="Inclusive">Cumulative inclusive duration.</param>
/// <param name="Calls">Number of outermost tagged calls.</param>
/// <param name="Maximum">Longest outermost tagged call.</param>
public readonly record struct MethodProfileContextSample(
    MethodProfileTag Tag,
    Type ContextType,
    TimeSpan Inclusive,
    long Calls,
    TimeSpan Maximum);

/// <summary>
/// Immutable data published for one completed logical tick.
/// </summary>
public sealed class MethodProfilerSnapshot
{
    private readonly MethodProfileSample[] _methodSamples;
    private readonly MethodProfileContextSample[] _contextSamples;

    internal MethodProfilerSnapshot(
        long sessionGeneration,
        string ownerName,
        long tickId,
        MethodProfileSample[] methodSamples,
        MethodProfileContextSample[] contextSamples,
        string? diagnostic)
    {
        SessionGeneration = sessionGeneration;
        OwnerName = ownerName;
        TickId = tickId;
        _methodSamples = methodSamples;
        _contextSamples = contextSamples;
        Diagnostic = diagnostic;
    }

    /// <summary>
    /// Gets the session generation that produced this snapshot.
    /// </summary>
    public long SessionGeneration { get; }

    /// <summary>
    /// Gets the owner of the session that produced this snapshot.
    /// </summary>
    public string OwnerName { get; }

    /// <summary>
    /// Gets the completed logical tick identifier.
    /// </summary>
    public long TickId { get; }

    /// <summary>
    /// Gets per-method, per-thread samples.
    /// </summary>
    public ReadOnlyMemory<MethodProfileSample> MethodSamples => _methodSamples;

    /// <summary>
    /// Gets tagged root-context samples.
    /// </summary>
    public ReadOnlyMemory<MethodProfileContextSample> ContextSamples => _contextSamples;

    /// <summary>
    /// Gets an optional runtime diagnostic associated with collection.
    /// </summary>
    public string? Diagnostic { get; }
}
