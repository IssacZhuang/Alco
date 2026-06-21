using System.Collections.Concurrent;
using System.Diagnostics;

namespace Alco.Profiler;

/// <summary>
/// Collects method timings for one exclusive consumer and publishes completed-tick snapshots.
/// </summary>
public sealed class MethodProfilerRuntime
{
    private sealed class SessionState
    {
        public required long Generation { get; init; }
        public required string OwnerName { get; init; }
        public required MethodProfileFilter Filter { get; init; }
        public required HashSet<ulong> EnabledMethodIds;
        public string? Diagnostic;
    }

    private sealed class TickState
    {
        public required long TickId { get; init; }
        public required long SessionGeneration { get; init; }
    }

    private struct Frame
    {
        public ulong MethodId;
        public long StartTimestamp;
        public long ChildTimestamp;
        public MethodProfileTag ContextTag;
        public RuntimeTypeHandle ContextTypeHandle;
        public bool IsContextRoot;
    }

    private struct MutableSample
    {
        public long Inclusive;
        public long Self;
        public long Calls;
        public long Maximum;
    }

    private readonly record struct ContextKey(MethodProfileTag Tag, RuntimeTypeHandle TypeHandle);

    private struct MutableContextSample
    {
        public long Inclusive;
        public long Calls;
        public long Maximum;
    }

    private sealed class ThreadState
    {
        public readonly List<Frame> Stack = new(32);
        public readonly Dictionary<ulong, MutableSample> MethodSamples = new(128);
        public readonly Dictionary<ContextKey, MutableContextSample> ContextSamples = new(32);
        public readonly int ThreadId;
        public long TickId;
        public long SessionGeneration;
        public int NormalContextDepth;
        public int ParallelContextDepth;

        public ThreadState(int threadId)
        {
            ThreadId = threadId;
        }

        public void Prepare(long tickId, long sessionGeneration)
        {
            if (TickId == tickId && SessionGeneration == sessionGeneration)
            {
                return;
            }

            Stack.Clear();
            MethodSamples.Clear();
            ContextSamples.Clear();
            NormalContextDepth = 0;
            ParallelContextDepth = 0;
            TickId = tickId;
            SessionGeneration = sessionGeneration;
        }

        public void ClearCompleted()
        {
            Stack.Clear();
            MethodSamples.Clear();
            ContextSamples.Clear();
            NormalContextDepth = 0;
            ParallelContextDepth = 0;
            TickId = 0;
            SessionGeneration = 0;
        }
    }

    private readonly object _gate = new();
    private readonly ConcurrentDictionary<ulong, MethodProfileMetadata> _metadata = new();
    private readonly ConcurrentDictionary<(RuntimeTypeHandle, string), bool> _runtimeContractCache = new();
    private readonly ThreadLocal<ThreadState> _threadStates =
        new(static () => new ThreadState(Environment.CurrentManagedThreadId), true);
    private readonly Func<long> _getTimestamp;
    private readonly long _timestampFrequency;
    private SessionState? _activeSession;
    private TickState? _activeTick;
    private MethodProfilerSnapshot? _lastSnapshot;
    private string? _instrumentationDiagnostic;
    private long _nextSessionGeneration;
    private long _nextTickId;

    /// <summary>
    /// Gets the process-wide profiler runtime used by woven hooks.
    /// </summary>
    public static MethodProfilerRuntime Instance { get; } = new();

    /// <summary>
    /// Initializes a profiler runtime using the high-resolution system timestamp source.
    /// </summary>
    public MethodProfilerRuntime()
        : this(Stopwatch.GetTimestamp, Stopwatch.Frequency)
    {
    }

    internal MethodProfilerRuntime(Func<long> getTimestamp, long timestampFrequency)
    {
        ArgumentNullException.ThrowIfNull(getTimestamp);
        if (timestampFrequency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timestampFrequency));
        }

        _getTimestamp = getTimestamp;
        _timestampFrequency = timestampFrequency;
    }

    /// <summary>
    /// Gets whether at least one woven method is registered and registration is healthy.
    /// </summary>
    public bool IsInstrumentationAvailable => !_metadata.IsEmpty && Volatile.Read(ref _instrumentationDiagnostic) == null;

    /// <summary>
    /// Gets an instrumentation-level diagnostic that prevents collection.
    /// </summary>
    public string? InstrumentationDiagnostic => Volatile.Read(ref _instrumentationDiagnostic);

    /// <summary>
    /// Gets the current profiling session owner, or null when idle.
    /// </summary>
    public string? CurrentOwner => Volatile.Read(ref _activeSession)?.OwnerName;

    /// <summary>
    /// Gets the diagnostic that disabled the active session, if any.
    /// </summary>
    public string? CurrentSessionDiagnostic => Volatile.Read(ref _activeSession)?.Diagnostic;

    /// <summary>
    /// Attempts to acquire the exclusive profiling session.
    /// </summary>
    /// <param name="ownerName">Diagnostic owner name.</param>
    /// <param name="filter">Methods selected for collection.</param>
    /// <param name="session">Created session when successful.</param>
    /// <param name="currentOwner">Existing owner when acquisition fails because the runtime is busy.</param>
    /// <returns>True when the session was acquired.</returns>
    public bool TryAcquireSession(
        string ownerName,
        MethodProfileFilter filter,
        out MethodProfilerSession? session,
        out string? currentOwner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerName);
        ArgumentNullException.ThrowIfNull(filter);

        lock (_gate)
        {
            if (!IsInstrumentationAvailable)
            {
                session = null;
                currentOwner = null;
                return false;
            }

            if (_activeSession != null)
            {
                session = null;
                currentOwner = _activeSession.OwnerName;
                return false;
            }

            var enabledMethodIds = new HashSet<ulong>();
            foreach (MethodProfileMetadata metadata in _metadata.Values)
            {
                if (filter.Includes(metadata))
                {
                    enabledMethodIds.Add(metadata.Id);
                }
            }

            long generation = ++_nextSessionGeneration;
            var state = new SessionState
            {
                Generation = generation,
                OwnerName = ownerName,
                Filter = filter,
                EnabledMethodIds = enabledMethodIds,
            };
            Volatile.Write(ref _activeSession, state);
            session = new MethodProfilerSession(TryReleaseSession, generation, ownerName);
            currentOwner = null;
            return true;
        }
    }

    /// <summary>
    /// Starts a logical tick collection boundary for the active session.
    /// </summary>
    /// <returns>A token that must be completed with <see cref="EndTick"/>.</returns>
    public MethodProfilerTickToken BeginTick()
    {
        SessionState? session = Volatile.Read(ref _activeSession);
        if (session == null || session.Diagnostic != null)
        {
            return default;
        }

        long tickId = Interlocked.Increment(ref _nextTickId);
        var tick = new TickState
        {
            TickId = tickId,
            SessionGeneration = session.Generation,
        };
        Volatile.Write(ref _activeTick, tick);
        return new MethodProfilerTickToken(tickId, session.Generation);
    }

    /// <summary>
    /// Completes a logical tick and atomically publishes its immutable snapshot.
    /// </summary>
    /// <param name="token">Token returned by <see cref="BeginTick"/>.</param>
    public void EndTick(MethodProfilerTickToken token)
    {
        if (!token.IsValid)
        {
            return;
        }

        TickState? tick = Interlocked.Exchange(ref _activeTick, null);
        SessionState? session = Volatile.Read(ref _activeSession);
        if (tick == null || session == null || tick.TickId != token.TickId ||
            session.Generation != token.SessionGeneration)
        {
            return;
        }

        var methodSamples = new List<MethodProfileSample>();
        var mergedContexts = new Dictionary<ContextKey, MutableContextSample>();
        foreach (ThreadState state in _threadStates.Values)
        {
            if (state.TickId != token.TickId || state.SessionGeneration != token.SessionGeneration)
            {
                continue;
            }

            if (state.Stack.Count != 0)
            {
                session.Diagnostic = "One or more profiler scopes were still active at EndTick.";
                state.ClearCompleted();
                continue;
            }

            foreach ((ulong methodId, MutableSample value) in state.MethodSamples)
            {
                methodSamples.Add(new MethodProfileSample(
                    methodId,
                    state.ThreadId,
                    ToTimeSpan(value.Inclusive),
                    ToTimeSpan(value.Self),
                    value.Calls,
                    ToTimeSpan(value.Maximum)));
            }

            foreach ((ContextKey key, MutableContextSample value) in state.ContextSamples)
            {
                mergedContexts.TryGetValue(key, out MutableContextSample merged);
                merged.Inclusive += value.Inclusive;
                merged.Calls += value.Calls;
                merged.Maximum = Math.Max(merged.Maximum, value.Maximum);
                mergedContexts[key] = merged;
            }

            state.ClearCompleted();
        }

        methodSamples.Sort(static (left, right) =>
        {
            int method = left.MethodId.CompareTo(right.MethodId);
            return method != 0 ? method : left.ThreadId.CompareTo(right.ThreadId);
        });

        MethodProfileContextSample[] contextSamples = mergedContexts
            .Select(pair => new MethodProfileContextSample(
                pair.Key.Tag,
                Type.GetTypeFromHandle(pair.Key.TypeHandle)!,
                ToTimeSpan(pair.Value.Inclusive),
                pair.Value.Calls,
                ToTimeSpan(pair.Value.Maximum)))
            .OrderBy(static sample => sample.Tag)
            .ThenBy(static sample => sample.ContextType.FullName, StringComparer.Ordinal)
            .ToArray();

        var snapshot = new MethodProfilerSnapshot(
            session.Generation,
            session.OwnerName,
            token.TickId,
            methodSamples.ToArray(),
            contextSamples,
            session.Diagnostic);
        Volatile.Write(ref _lastSnapshot, snapshot);
    }

    /// <summary>
    /// Attempts to get the latest completed-tick snapshot.
    /// </summary>
    /// <param name="snapshot">Latest snapshot when available.</param>
    /// <returns>True when a completed snapshot exists.</returns>
    public bool TryGetLatestSnapshot(out MethodProfilerSnapshot? snapshot)
    {
        snapshot = Volatile.Read(ref _lastSnapshot);
        return snapshot != null;
    }

    /// <summary>
    /// Attempts to resolve registered metadata for a method sample identifier.
    /// </summary>
    /// <param name="methodId">Method identifier from a snapshot.</param>
    /// <param name="metadata">Registered metadata when found.</param>
    /// <returns>True when the method is registered.</returns>
    public bool TryGetMethodMetadata(ulong methodId, out MethodProfileMetadata? metadata)
    {
        return _metadata.TryGetValue(methodId, out metadata);
    }

    /// <summary>
    /// Registers metadata emitted by a woven module.
    /// </summary>
    /// <param name="metadata">Method metadata.</param>
    public void RegisterMethod(MethodProfileMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        MethodProfileMetadata registered = _metadata.GetOrAdd(metadata.Id, metadata);
        if (registered != metadata)
        {
            ReportInstrumentationFault(
                $"Method profiler ID collision: {registered.DeclaringTypeName}.{registered.Signature} and " +
                $"{metadata.DeclaringTypeName}.{metadata.Signature} share {metadata.Id}.");
            return;
        }

        lock (_gate)
        {
            SessionState? session = _activeSession;
            if (session != null && session.Filter.Includes(metadata))
            {
                var enabledMethodIds = new HashSet<ulong>(Volatile.Read(ref session.EnabledMethodIds))
                {
                    metadata.Id,
                };
                Volatile.Write(ref session.EnabledMethodIds, enabledMethodIds);
            }
        }
    }

    /// <summary>
    /// Begins one instrumented method interval.
    /// </summary>
    /// <param name="methodId">Registered method identifier.</param>
    /// <param name="tags">Special aggregation tags.</param>
    /// <param name="context">Optional captured instance.</param>
    /// <returns>A token passed to <see cref="Exit"/>.</returns>
    public MethodProfileToken Enter(ulong methodId, MethodProfileTag tags, object? context)
    {
        SessionState? session = Volatile.Read(ref _activeSession);
        TickState? tick = Volatile.Read(ref _activeTick);
        if (session == null || tick == null || session.Generation != tick.SessionGeneration ||
            !Volatile.Read(ref session.EnabledMethodIds).Contains(methodId))
        {
            return default;
        }

        ThreadState state = _threadStates.Value!;
        state.Prepare(tick.TickId, session.Generation);

        MethodProfileTag contextTag = ResolveContextTag(methodId, tags, context);
        bool isContextRoot = false;
        RuntimeTypeHandle contextTypeHandle = default;
        if (contextTag != MethodProfileTag.None && context != null)
        {
            contextTypeHandle = context.GetType().TypeHandle;
            if (contextTag == MethodProfileTag.ComponentTick)
            {
                isContextRoot = state.NormalContextDepth++ == 0;
            }
            else if (contextTag == MethodProfileTag.ComponentParallelTick)
            {
                isContextRoot = state.ParallelContextDepth++ == 0;
            }
        }

        int stackIndex = state.Stack.Count;
        state.Stack.Add(new Frame
        {
            MethodId = methodId,
            StartTimestamp = _getTimestamp(),
            ChildTimestamp = 0,
            ContextTag = contextTag,
            ContextTypeHandle = contextTypeHandle,
            IsContextRoot = isContextRoot,
        });
        return new MethodProfileToken(tick.TickId, session.Generation, stackIndex);
    }

    /// <summary>
    /// Completes one instrumented method interval.
    /// </summary>
    /// <param name="token">Token returned by <see cref="Enter"/>.</param>
    public void Exit(MethodProfileToken token)
    {
        ThreadState state = _threadStates.Value!;
        if (state.TickId != token.TickId || state.SessionGeneration != token.SessionGeneration ||
            state.Stack.Count != token.StackIndex + 1)
        {
            return;
        }

        int index = state.Stack.Count - 1;
        Frame frame = state.Stack[index];
        state.Stack.RemoveAt(index);
        long inclusive = Math.Max(0, _getTimestamp() - frame.StartTimestamp);
        long self = Math.Max(0, inclusive - frame.ChildTimestamp);

        state.MethodSamples.TryGetValue(frame.MethodId, out MutableSample sample);
        sample.Inclusive += inclusive;
        sample.Self += self;
        sample.Calls++;
        sample.Maximum = Math.Max(sample.Maximum, inclusive);
        state.MethodSamples[frame.MethodId] = sample;

        if (state.Stack.Count != 0)
        {
            Frame parent = state.Stack[^1];
            parent.ChildTimestamp += inclusive;
            state.Stack[^1] = parent;
        }

        if (frame.ContextTag == MethodProfileTag.ComponentTick)
        {
            state.NormalContextDepth--;
        }
        else if (frame.ContextTag == MethodProfileTag.ComponentParallelTick)
        {
            state.ParallelContextDepth--;
        }

        if (!frame.IsContextRoot)
        {
            return;
        }

        var key = new ContextKey(frame.ContextTag, frame.ContextTypeHandle);
        state.ContextSamples.TryGetValue(key, out MutableContextSample contextSample);
        contextSample.Inclusive += inclusive;
        contextSample.Calls++;
        contextSample.Maximum = Math.Max(contextSample.Maximum, inclusive);
        state.ContextSamples[key] = contextSample;
    }

    /// <summary>
    /// Attempts to release the active session when its generation matches the caller's lease.
    /// </summary>
    /// <param name="generation">Session generation returned by acquisition.</param>
    /// <returns>True when the matching session was released.</returns>
    public bool TryReleaseSession(long generation)
    {
        lock (_gate)
        {
            if (_activeSession?.Generation != generation)
            {
                return false;
            }

            Volatile.Write(ref _activeTick, null);
            Volatile.Write(ref _activeSession, null);
            return true;
        }
    }

    /// <summary>
    /// Reports an internal collection fault and disables the current session's tick collection.
    /// </summary>
    /// <param name="diagnostic">Fault description exposed to the consumer.</param>
    public void ReportSessionFault(string diagnostic)
    {
        lock (_gate)
        {
            if (_activeSession != null)
            {
                _activeSession.Diagnostic ??= diagnostic;
            }
            Volatile.Write(ref _activeTick, null);
        }
    }

    /// <summary>
    /// Reports a registration-level fault and disables instrumentation for the process.
    /// </summary>
    /// <param name="diagnostic">Fault description exposed to consumers.</param>
    public void ReportInstrumentationFault(string diagnostic)
    {
        Volatile.Write(ref _instrumentationDiagnostic, diagnostic);
        lock (_gate)
        {
            Volatile.Write(ref _activeTick, null);
            Volatile.Write(ref _activeSession, null);
        }
    }

    private MethodProfileTag ResolveContextTag(ulong methodId, MethodProfileTag tags, object? context)
    {
        MethodProfileTag contextTags = tags &
            (MethodProfileTag.ComponentTick | MethodProfileTag.ComponentParallelTick);
        if (contextTags == MethodProfileTag.None || context == null ||
            !_metadata.TryGetValue(methodId, out MethodProfileMetadata? metadata) ||
            metadata.RequiredRuntimeInterface == null)
        {
            return SelectSingleContextTag(contextTags);
        }

        Type contextType = context.GetType();
        bool implementsContract = _runtimeContractCache.GetOrAdd(
            (contextType.TypeHandle, metadata.RequiredRuntimeInterface),
            static key => ImplementsInterface(Type.GetTypeFromHandle(key.Item1)!, key.Item2));
        return implementsContract ? SelectSingleContextTag(contextTags) : MethodProfileTag.None;
    }

    private static bool ImplementsInterface(Type contextType, string interfaceName)
    {
        Type[] interfaces = contextType.GetInterfaces();
        for (int i = 0; i < interfaces.Length; i++)
        {
            Type candidate = interfaces[i];
            if (string.Equals(candidate.FullName, interfaceName, StringComparison.Ordinal) ||
                string.Equals(candidate.AssemblyQualifiedName, interfaceName, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private static MethodProfileTag SelectSingleContextTag(MethodProfileTag tags)
    {
        if ((tags & MethodProfileTag.ComponentTick) != 0)
        {
            return MethodProfileTag.ComponentTick;
        }
        if ((tags & MethodProfileTag.ComponentParallelTick) != 0)
        {
            return MethodProfileTag.ComponentParallelTick;
        }
        return MethodProfileTag.None;
    }

    private TimeSpan ToTimeSpan(long timestampTicks)
    {
        return TimeSpan.FromSeconds((double)timestampTicks / _timestampFrequency);
    }
}
