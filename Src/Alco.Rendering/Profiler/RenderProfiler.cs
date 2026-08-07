using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Alco.Rendering;

/// <summary>
/// A lightweight handle to a registered profiling counter. Obtained once from
/// <see cref="RenderProfiler.RegisterCounter"/> during initialization and used in
/// hot-path <see cref="RenderProfiler.PushValue"/> calls to avoid string allocations.
/// </summary>
public readonly struct RenderProfileCounterId
{
    internal readonly int Value;

    internal RenderProfileCounterId(int value)
    {
        Value = value;
    }

    /// <summary>Whether this handle refers to a valid counter (0 is the invalid sentinel).</summary>
    public bool IsValid => Value > 0;

    /// <summary>An invalid sentinel returned when no counter has been registered.</summary>
    public static RenderProfileCounterId Invalid => default;
}

/// <summary>
/// A read-only snapshot of all counter values for a single frame. The arrays are
/// pre-allocated and reused across frames, so consuming this struct never allocates.
/// </summary>
public readonly struct RenderProfileSnapshot
{
    /// <summary>The number of valid entries in the parallel arrays.</summary>
    public readonly int Count;

    /// <summary>The group name for each counter (e.g. "Pipeline", "VoxelGI").</summary>
    public readonly string[] Groups;

    /// <summary>The display name for each counter (e.g. "Shadow", "Inject").</summary>
    public readonly string[] Names;

    /// <summary>The value for each counter in milliseconds.</summary>
    public readonly double[] Values;

    internal RenderProfileSnapshot(int count, string[] groups, string[] names, double[] values)
    {
        Count = count;
        Groups = groups;
        Names = names;
        Values = values;
    }
}

/// <summary>
/// A zero-allocation hub for collecting per-frame render performance data.
/// Counter names are registered once during initialization and exchanged for
/// integer IDs, so the per-frame <see cref="PushValue"/> hot path performs only
/// a single array write with no string operations.
/// <para>
/// Internally uses double buffering: the current frame writes into
/// <c>_current</c> while readers observe the previously published
/// <c>_published</c> buffer. <see cref="BeginFrame"/> clears the current buffer;
/// <see cref="EndFrame"/> swaps and publishes a <see cref="RenderProfileSnapshot"/>.
/// </para>
/// <para>
/// Push and frame-lifecycle methods are not thread-safe — they must be called from
/// the render thread, consistent with <see cref="RenderContext"/>. The snapshot
/// returned by <see cref="GetSnapshot"/> may be read from any thread after
/// <see cref="EndFrame"/> returns.
/// </para>
/// </summary>
public sealed class RenderProfiler
{
    // Initial capacity for counter arrays; grows on demand (registration phase only).
    private const int InitialCapacity = 32;

    // Snapshot publication interval — the UI sees fresh data every 0.5s so the
    // numbers are readable rather than flickering every frame.
        private const double PublicationIntervalSeconds = 1.0;

    // Counter metadata — only grows during RegisterCounter calls (never on the hot path).
    private string[] _groups;
    private string[] _names;
    private int _count;

    // Double-buffered value arrays.
    private double[] _current;     // Cleared by BeginFrame, written by PushValue.
    private double[] _published;   // Swapped from _current on EndFrame for reader access.

    // Throttle timer for snapshot publication.
    private readonly Stopwatch _publishTimer = Stopwatch.StartNew();

    // Pre-allocated snapshot struct reused every EndFrame — no per-frame allocation.
    private RenderProfileSnapshot _snapshot;

    /// <summary>
    /// Create a render profiler with default pre-allocated capacity.
    /// </summary>
    public RenderProfiler()
    {
        _groups = new string[InitialCapacity];
        _names = new string[InitialCapacity];
        _current = new double[InitialCapacity];
        _published = new double[InitialCapacity];
        _snapshot = new RenderProfileSnapshot(0, _groups, _names, _published);
    }

    /// <summary>
    /// The number of registered counters.
    /// </summary>
    public int CounterCount => _count;

    /// <summary>
    /// Register a named counter and return a handle for zero-allocation hot-path pushes.
    /// Call during initialization only (e.g. pipeline/plugin constructor or first Execute).
    /// </summary>
    /// <param name="group">The group label for display (e.g. "Pipeline", "VoxelGI").</param>
    /// <param name="name">The counter display name (e.g. "Shadow", "Inject").</param>
    /// <returns>A handle for use with <see cref="PushValue"/>.</returns>
    public RenderProfileCounterId RegisterCounter(string group, string name)
    {
        int id = ++_count; // 1-based; 0 = Invalid sentinel.
        EnsureCapacity(id);
        _groups[id - 1] = group;
        _names[id - 1] = name;
        return new RenderProfileCounterId(id);
    }

    /// <summary>
    /// Clear the current-frame buffer. Called by the pipeline at the beginning of each frame
    /// (typically the first shadow pass). Values pushed after this call accumulate until
    /// <see cref="EndFrame"/>.
    /// </summary>
    public void BeginFrame()
    {
        if (_count > 0)
        {
            Array.Clear(_current, 0, _count);
        }
    }

    /// <summary>
    /// Push a counter value for the current frame. O(1), zero-allocation.
    /// </summary>
    /// <param name="id">The counter handle from <see cref="RegisterCounter"/>.</param>
    /// <param name="milliseconds">The measured duration in milliseconds.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PushValue(RenderProfileCounterId id, double milliseconds)
    {
        if (id.IsValid)
        {
            _current[id.Value - 1] = milliseconds;
        }
    }

    /// <summary>
    /// Publish the current-frame buffer into the snapshot slot if the publication
    /// interval (0.5s) has elapsed. Called by the pipeline at the end of each
    /// frame. Between publications, <see cref="GetSnapshot"/> returns the last
    /// published data so the UI shows stable numbers instead of per-frame jitter.
    /// </summary>
    public void EndFrame()
    {
        if (_publishTimer.Elapsed.TotalSeconds >= PublicationIntervalSeconds)
        {
            (_published, _current) = (_current, _published);
            _snapshot = new RenderProfileSnapshot(_count, _groups, _names, _published);
            _publishTimer.Restart();
        }
    }

    /// <summary>
    /// Get the most recently published frame snapshot. Returns a reference to the internally
    /// pre-allocated struct, so this call never allocates.
    /// </summary>
    /// <returns>A read-only reference to the current snapshot.</returns>
    public ref readonly RenderProfileSnapshot GetSnapshot()
    {
        return ref _snapshot;
    }

    /// <summary>
    /// Grow all internal arrays to at least <paramref name="needed"/> elements.
    /// Called only during RegisterCounter (never on the hot path).
    /// </summary>
    private void EnsureCapacity(int needed)
    {
        if (needed <= _groups.Length)
        {
            return;
        }

        int newCapacity = _groups.Length * 2;
        while (newCapacity < needed)
        {
            newCapacity *= 2;
        }

        Array.Resize(ref _groups, newCapacity);
        Array.Resize(ref _names, newCapacity);
        Array.Resize(ref _current, newCapacity);
        Array.Resize(ref _published, newCapacity);
    }
}
