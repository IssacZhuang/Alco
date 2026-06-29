namespace Alco.Profiler;

/// <summary>
/// Marks an assembly whose method bodies were instrumented by the profiler build tool.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class MethodProfilerInstrumentedAttribute : Attribute
{
    /// <summary>
    /// Initializes the marker with the instrumentation format version.
    /// </summary>
    /// <param name="version">Instrumentation format version.</param>
    public MethodProfilerInstrumentedAttribute(string version)
    {
        Version = version;
    }

    /// <summary>
    /// Gets the instrumentation format version.
    /// </summary>
    public string Version { get; }
}

/// <summary>
/// Identifies special runtime aggregation channels attached to an instrumented method.
/// </summary>
[Flags]
public enum MethodProfileTag
{
    /// <summary>
    /// The method has no special aggregation channel.
    /// </summary>
    None = 0,

    /// <summary>
    /// The method participates in normal component tick aggregation.
    /// </summary>
    ComponentTick = 1 << 0,

    /// <summary>
    /// The method participates in parallel component tick aggregation.
    /// </summary>
    ComponentParallelTick = 1 << 1,
}

/// <summary>
/// Describes one method registered by an instrumented module.
/// </summary>
/// <param name="Id">Deterministic method identifier.</param>
/// <param name="AssemblyName">Assembly containing the method body.</param>
/// <param name="DeclaringTypeName">Full declaring type name.</param>
/// <param name="MethodName">Source-facing method name.</param>
/// <param name="Signature">Normalized method signature.</param>
/// <param name="Tags">Special aggregation tags.</param>
/// <param name="RequiredRuntimeInterface">Optional interface required on a captured runtime type.</param>
public sealed record MethodProfileMetadata(
    ulong Id,
    string AssemblyName,
    string DeclaringTypeName,
    string MethodName,
    string Signature,
    MethodProfileTag Tags,
    string? RequiredRuntimeInterface);

/// <summary>
/// Selects registered methods when a profiling session is acquired.
/// </summary>
public sealed class MethodProfileFilter
{
    private readonly Func<MethodProfileMetadata, bool> _predicate;

    /// <summary>
    /// Initializes a filter from a C# predicate evaluated only when session membership changes.
    /// </summary>
    /// <param name="predicate">Predicate selecting method metadata.</param>
    public MethodProfileFilter(Func<MethodProfileMetadata, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        _predicate = predicate;
    }

    /// <summary>
    /// Creates a filter that selects methods containing at least one requested tag.
    /// </summary>
    /// <param name="tags">Tags accepted by the filter.</param>
    /// <returns>A filter for the requested tags.</returns>
    public static MethodProfileFilter ByTags(MethodProfileTag tags)
    {
        return new MethodProfileFilter(metadata => (metadata.Tags & tags) != 0);
    }

    /// <summary>
    /// Creates a filter that selects every instrumented method.
    /// </summary>
    /// <returns>A filter accepting every method.</returns>
    public static MethodProfileFilter All()
    {
        return new MethodProfileFilter(static _ => true);
    }

    /// <summary>
    /// Evaluates whether registered metadata is selected by this filter.
    /// </summary>
    /// <param name="metadata">Registered method metadata.</param>
    /// <returns>True when the method is selected.</returns>
    public bool Includes(MethodProfileMetadata metadata)
    {
        return _predicate(metadata);
    }
}

/// <summary>
/// Identifies one active logical tick boundary.
/// </summary>
public readonly struct MethodProfilerTickToken
{
    /// <summary>
    /// Initializes a logical tick token.
    /// </summary>
    /// <param name="tickId">Logical tick identifier.</param>
    /// <param name="sessionGeneration">Owning session generation.</param>
    public MethodProfilerTickToken(long tickId, long sessionGeneration)
    {
        TickId = tickId;
        SessionGeneration = sessionGeneration;
    }

    /// <summary>
    /// Gets the logical tick identifier.
    /// </summary>
    public long TickId { get; }

    /// <summary>
    /// Gets the owning session generation.
    /// </summary>
    public long SessionGeneration { get; }

    /// <summary>
    /// Gets whether this token represents an active boundary.
    /// </summary>
    public bool IsValid => TickId != 0;
}

/// <summary>
/// Identifies one instrumented method interval.
/// </summary>
public readonly struct MethodProfileToken
{
    /// <summary>
    /// Initializes a method interval token.
    /// </summary>
    /// <param name="tickId">Logical tick identifier.</param>
    /// <param name="sessionGeneration">Owning session generation.</param>
    /// <param name="stackIndex">Thread-local stack position.</param>
    public MethodProfileToken(long tickId, long sessionGeneration, int stackIndex)
    {
        TickId = tickId;
        SessionGeneration = sessionGeneration;
        StackIndex = stackIndex;
    }

    /// <summary>
    /// Gets the logical tick identifier.
    /// </summary>
    public long TickId { get; }

    /// <summary>
    /// Gets the owning session generation.
    /// </summary>
    public long SessionGeneration { get; }

    /// <summary>
    /// Gets the thread-local stack position.
    /// </summary>
    public int StackIndex { get; }

    /// <summary>
    /// Gets whether this token represents a measured interval.
    /// </summary>
    public bool IsValid => TickId != 0;
}
