using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace Alco;

/// <summary>
/// Categorizes an event recorded by <see cref="JobScheduler"/> diagnostics.
/// </summary>
public enum JobProfileEventKind : byte
{
    /// <summary>Job or callback execution on a dedicated worker thread.</summary>
    JobExecute = 0,

    /// <summary>Frame-lane job or callback execution performed by the main thread while waiting.</summary>
    MainHelp = 1,

    /// <summary>Main-thread busy spin while waiting for job completion.</summary>
    MainWaitSpin = 2,

    /// <summary>Main-thread kernel wait (<see cref="WaitHandle.WaitOne()"/>) while waiting for job completion.</summary>
    MainWaitSleep = 3,

    /// <summary>Instant phase or lifecycle marker recorded on the main thread.</summary>
    Marker = 4,
}

/// <summary>
/// Identifies the underlying work item shape for an execution profile span.
/// </summary>
public enum JobProfileWorkKind : byte
{
    /// <summary>No work shape (used for wait spans and markers).</summary>
    None = 0,

    /// <summary>Single-execution <see cref="IJob"/>.</summary>
    Single = 1,

    /// <summary>Indexed <see cref="IBatchJob"/>.</summary>
    Batch = 2,

    /// <summary>Contiguous-range <see cref="IBatchRangeJob"/>.</summary>
    BatchRange = 3,

    /// <summary>Completion callback only.</summary>
    CallbackOnly = 4,
}

/// <summary>
/// Immutable execution, wait, or marker event captured from <see cref="JobScheduler"/>.
/// </summary>
public readonly struct JobProfileEvent
{
    /// <summary>Flag indicating the job is still running at the end of the captured window.</summary>
    public const byte FlagInProgress = 1;

    /// <summary>Flag indicating the job recorded an exception during execution.</summary>
    public const byte FlagFailed = 2;

    /// <summary>Start timestamp in <see cref="Stopwatch"/> ticks.</summary>
    public readonly long StartTimestamp;

    /// <summary>End timestamp in <see cref="Stopwatch"/> ticks.</summary>
    public readonly long EndTimestamp;

    /// <summary>Number of batch items or single executions processed during this span.</summary>
    public readonly int ItemsProcessed;

    /// <summary>Number of stolen chunks or execution units processed during this span.</summary>
    public readonly int ChunksProcessed;

    /// <summary>Interned job type or marker name identifier resolved via <see cref="JobScheduler.ProfileSnapshot.GetTypeName"/>.</summary>
    public readonly ushort TypeId;

    /// <summary>Zero-based track index (0 = main thread, followed by Frame workers, then Background workers).</summary>
    public readonly byte TrackIndex;

    /// <summary>Event category.</summary>
    public readonly JobProfileEventKind Kind;

    /// <summary>Execution lane associated with the event.</summary>
    public readonly JobLane Lane;

    /// <summary>Underlying job work shape.</summary>
    public readonly JobProfileWorkKind WorkKind;

    /// <summary>Bitmask of <see cref="FlagInProgress"/> and <see cref="FlagFailed"/>.</summary>
    public readonly byte Flags;

    /// <summary>Initializes a new <see cref="JobProfileEvent"/>.</summary>
    public JobProfileEvent(
        long startTimestamp,
        long endTimestamp,
        int itemsProcessed,
        int chunksProcessed,
        ushort typeId,
        byte trackIndex,
        JobProfileEventKind kind,
        JobLane lane,
        JobProfileWorkKind workKind,
        byte flags)
    {
        StartTimestamp = startTimestamp;
        EndTimestamp = endTimestamp;
        ItemsProcessed = itemsProcessed;
        ChunksProcessed = chunksProcessed;
        TypeId = typeId;
        TrackIndex = trackIndex;
        Kind = kind;
        Lane = lane;
        WorkKind = workKind;
        Flags = flags;
    }

    /// <summary>Gets whether the span was still executing when the snapshot window ended.</summary>
    public bool IsInProgress => (Flags & FlagInProgress) != 0;

    /// <summary>Gets whether an exception was recorded during this span.</summary>
    public bool Failed => (Flags & FlagFailed) != 0;

    /// <summary>Gets the event duration in milliseconds.</summary>
    public double DurationMs => EndTimestamp > StartTimestamp
        ? (EndTimestamp - StartTimestamp) * 1000.0 / Stopwatch.Frequency
        : 0.0;
}

/// <summary>
/// Per-thread utilization and execution summary within one captured snapshot window.
/// </summary>
public readonly struct JobTrackStats
{
    /// <summary>Zero-based track index.</summary>
    public readonly byte TrackIndex;

    /// <summary>Lane served by this track.</summary>
    public readonly JobLane Lane;

    /// <summary>Whether this track represents the producer/main thread.</summary>
    public readonly bool IsMainThread;

    /// <summary>Time spent executing jobs (or helping on the main thread) within the window, in milliseconds.</summary>
    public readonly double BusyMs;

    /// <summary>Time spent spin-waiting within the window, in milliseconds.</summary>
    public readonly double SpinMs;

    /// <summary>Time spent blocked/sleeping within the window, in milliseconds.</summary>
    public readonly double SleepMs;

    /// <summary>Number of job execution spans recorded on this track during the window.</summary>
    public readonly int Executions;

    /// <summary>Total chunks processed on this track during the window.</summary>
    public readonly int Chunks;

    /// <summary>Total items processed on this track during the window.</summary>
    public readonly int Items;

    /// <summary>Number of kernel wakeups issued to this worker during the window.</summary>
    public readonly int WakeCount;

    /// <summary>Initializes a new <see cref="JobTrackStats"/>.</summary>
    public JobTrackStats(
        byte trackIndex,
        JobLane lane,
        bool isMainThread,
        double busyMs,
        double spinMs,
        double sleepMs,
        int executions,
        int chunks,
        int items,
        int wakeCount)
    {
        TrackIndex = trackIndex;
        Lane = lane;
        IsMainThread = isMainThread;
        BusyMs = busyMs;
        SpinMs = spinMs;
        SleepMs = sleepMs;
        Executions = executions;
        Chunks = chunks;
        Items = items;
        WakeCount = wakeCount;
    }
}

/// <summary>
/// Aggregated CPU timing and throughput statistics for one job type within a snapshot window.
/// </summary>
public readonly struct JobTypeStats
{
    /// <summary>Interned job type identifier.</summary>
    public readonly ushort TypeId;

    /// <summary>Execution lane of the job.</summary>
    public readonly JobLane Lane;

    /// <summary>Work shape of the job.</summary>
    public readonly JobProfileWorkKind WorkKind;

    /// <summary>Total execution spans (across all threads) in the window.</summary>
    public readonly int Executions;

    /// <summary>Total chunks executed across all threads in the window.</summary>
    public readonly int Chunks;

    /// <summary>Total items processed across all threads in the window.</summary>
    public readonly int Items;

    /// <summary>Sum of execution durations across all threads, in milliseconds.</summary>
    public readonly double TotalCpuMs;

    /// <summary>Longest individual execution span in the window, in milliseconds.</summary>
    public readonly double MaxSingleMs;

    /// <summary>Initializes a new <see cref="JobTypeStats"/>.</summary>
    public JobTypeStats(
        ushort typeId,
        JobLane lane,
        JobProfileWorkKind workKind,
        int executions,
        int chunks,
        int items,
        double totalCpuMs,
        double maxSingleMs)
    {
        TypeId = typeId;
        Lane = lane;
        WorkKind = workKind;
        Executions = executions;
        Chunks = chunks;
        Items = items;
        TotalCpuMs = totalCpuMs;
        MaxSingleMs = maxSingleMs;
    }

    /// <summary>Gets the average execution duration per span in milliseconds.</summary>
    public double AvgMs => Executions > 0 ? TotalCpuMs / Executions : 0.0;
}

/// <summary>
/// Point-in-time and per-window scheduler queue, slot, edge, and wakeup counters.
/// </summary>
public readonly struct JobSchedulerCounters
{
    /// <summary>Configured maximum number of concurrent job slots.</summary>
    public readonly int SlotCapacity;

    /// <summary>Number of currently allocated job slots at capture time.</summary>
    public readonly int ActiveSlots;

    /// <summary>Peak concurrent allocated job slots observed during the window.</summary>
    public readonly int PeakActiveSlots;

    /// <summary>Configured maximum number of dependency edges.</summary>
    public readonly int EdgeCapacity;

    /// <summary>Number of allocated dependency edges at capture time.</summary>
    public readonly int UsedEdges;

    /// <summary>Approximate queued ticket count in the Frame lane at capture time.</summary>
    public readonly int FrameQueueDepth;

    /// <summary>Ring buffer capacity of the Frame lane queue.</summary>
    public readonly int FrameQueueCapacity;

    /// <summary>Approximate queued ticket count in the Background lane at capture time.</summary>
    public readonly int BackgroundQueueDepth;

    /// <summary>Ring buffer capacity of the Background lane queue.</summary>
    public readonly int BackgroundQueueCapacity;

    /// <summary>Incomplete jobs currently active in the Frame lane at capture time.</summary>
    public readonly int ActiveFrameJobs;

    /// <summary>Incomplete jobs currently active in the Background lane at capture time.</summary>
    public readonly int ActiveBackgroundJobs;

    /// <summary>Number of Frame workers currently parked in sleep at capture time.</summary>
    public readonly int FrameWorkersSleeping;

    /// <summary>Number of Background workers currently parked in sleep at capture time.</summary>
    public readonly int BackgroundWorkersSleeping;

    /// <summary>Jobs scheduled onto the Frame lane during the window.</summary>
    public readonly int JobsScheduledFrame;

    /// <summary>Jobs scheduled onto the Background lane during the window.</summary>
    public readonly int JobsScheduledBackground;

    /// <summary>Dependency fences allocated during the window.</summary>
    public readonly int FencesCreated;

    /// <summary>Kernel wakeups issued to Frame workers during the window.</summary>
    public readonly int FrameWakeups;

    /// <summary>Kernel wakeups issued to Background workers during the window.</summary>
    public readonly int BackgroundWakeups;

    /// <summary>Ring-buffer events overwritten before capture during the window.</summary>
    public readonly int DroppedEvents;

    /// <summary>Initializes a new <see cref="JobSchedulerCounters"/>.</summary>
    public JobSchedulerCounters(
        int slotCapacity,
        int activeSlots,
        int peakActiveSlots,
        int edgeCapacity,
        int usedEdges,
        int frameQueueDepth,
        int frameQueueCapacity,
        int backgroundQueueDepth,
        int backgroundQueueCapacity,
        int activeFrameJobs,
        int activeBackgroundJobs,
        int frameWorkersSleeping,
        int backgroundWorkersSleeping,
        int jobsScheduledFrame,
        int jobsScheduledBackground,
        int fencesCreated,
        int frameWakeups,
        int backgroundWakeups,
        int droppedEvents)
    {
        SlotCapacity = slotCapacity;
        ActiveSlots = activeSlots;
        PeakActiveSlots = peakActiveSlots;
        EdgeCapacity = edgeCapacity;
        UsedEdges = usedEdges;
        FrameQueueDepth = frameQueueDepth;
        FrameQueueCapacity = frameQueueCapacity;
        BackgroundQueueDepth = backgroundQueueDepth;
        BackgroundQueueCapacity = backgroundQueueCapacity;
        ActiveFrameJobs = activeFrameJobs;
        ActiveBackgroundJobs = activeBackgroundJobs;
        FrameWorkersSleeping = frameWorkersSleeping;
        BackgroundWorkersSleeping = backgroundWorkersSleeping;
        JobsScheduledFrame = jobsScheduledFrame;
        JobsScheduledBackground = jobsScheduledBackground;
        FencesCreated = fencesCreated;
        FrameWakeups = frameWakeups;
        BackgroundWakeups = backgroundWakeups;
        DroppedEvents = droppedEvents;
    }
}

public sealed partial class JobScheduler
{
    private volatile bool _profilingEnabled;
    private JobProfiler? _profiler;

    /// <summary>
    /// Gets or sets whether lock-free execution, wait, and queue profiling is enabled for this scheduler.
    /// </summary>
    public bool IsProfilingEnabled
    {
        get => _profilingEnabled;
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (value == _profilingEnabled)
            {
                return;
            }

            if (value)
            {
                _profiler ??= new JobProfiler(_frameLane.WorkerCount, _backgroundLane.WorkerCount);
                _profiler.Reset(Stopwatch.GetTimestamp(), _activeSlotCount);
                _profilingEnabled = true;
            }
            else
            {
                _profilingEnabled = false;
            }
        }
    }

    /// <summary>
    /// Records an instant diagnostic marker on the main-thread track when profiling is enabled.
    /// Must be called from the scheduler's producer thread.
    /// </summary>
    /// <param name="name">Display name of the marker.</param>
    public void RecordProfileMarker(string name)
    {
        if (!_profilingEnabled || string.IsNullOrEmpty(name))
        {
            return;
        }

        JobProfiler? profiler = _profiler;
        if (profiler == null)
        {
            return;
        }

        ushort typeId = profiler.RegisterMarkerName(name);
        long now = Stopwatch.GetTimestamp();
        profiler.RecordMarker(now, typeId);
    }

    /// <summary>
    /// Captures all diagnostics recorded since the previous capture into a reusable <see cref="ProfileSnapshot"/>.
    /// Must be called from the scheduler's producer thread.
    /// </summary>
    /// <param name="target">The reusable snapshot instance to populate.</param>
    /// <returns><see langword="true"/> when profiling was active and the snapshot was captured; otherwise <see langword="false"/>.</returns>
    public bool TryCaptureProfileSnapshot(ProfileSnapshot target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.TryCapture(this);
    }

    /// <summary>
    /// Reusable, zero-steady-state-allocation snapshot of one <see cref="JobScheduler"/> capture window.
    /// </summary>
    public sealed class ProfileSnapshot
    {
        private readonly List<JobTrackStats> _tracks = new();
        private readonly List<JobProfileEvent> _events = new();
        private readonly List<JobTypeStats> _typeStats = new();
        private readonly List<string> _typeNames = new();
        private readonly Dictionary<(ushort TypeId, byte Lane, byte WorkKind), int> _typeIndexScratch = new();
        private string[] _trackNames = [];

        /// <summary>Gets the window start timestamp in <see cref="Stopwatch"/> ticks.</summary>
        public long WindowStartTimestamp { get; private set; }

        /// <summary>Gets the window end timestamp in <see cref="Stopwatch"/> ticks.</summary>
        public long WindowEndTimestamp { get; private set; }

        /// <summary>Gets the elapsed window duration in milliseconds.</summary>
        public double WindowDurationMs { get; private set; }

        /// <summary>Gets the Frame worker thread count covered by this snapshot.</summary>
        public int FrameWorkerCount { get; private set; }

        /// <summary>Gets the Background worker thread count covered by this snapshot.</summary>
        public int BackgroundWorkerCount { get; private set; }

        /// <summary>Gets the scheduler queue, slot, edge, and wakeup counters for this window.</summary>
        public JobSchedulerCounters Counters { get; private set; }

        /// <summary>Gets per-track utilization and execution statistics (index 0 is Main).</summary>
        public IReadOnlyList<JobTrackStats> Tracks => _tracks;

        /// <summary>Gets all execution, main-wait, and marker events drained in this window.</summary>
        public IReadOnlyList<JobProfileEvent> Events => _events;

        /// <summary>Gets per-job-type CPU summaries sorted by total CPU time descending.</summary>
        public IReadOnlyList<JobTypeStats> TypeStats => _typeStats;

        /// <summary>Gets total time the main thread spent spin-waiting and sleeping on jobs in this window, in milliseconds.</summary>
        public double TotalMainWaitMs => _tracks.Count > 0 ? _tracks[0].SpinMs + _tracks[0].SleepMs : 0.0;

        /// <summary>Gets total time the main thread spent work-helping Frame jobs in this window, in milliseconds.</summary>
        public double TotalMainHelpMs => _tracks.Count > 0 ? _tracks[0].BusyMs : 0.0;

        /// <summary>Gets total CPU busy time across all dedicated worker threads in this window, in milliseconds.</summary>
        public double TotalWorkerCpuMs
        {
            get
            {
                double sum = 0.0;
                for (int i = 1; i < _tracks.Count; i++)
                {
                    sum += _tracks[i].BusyMs;
                }
                return sum;
            }
        }

        /// <summary>Gets the average busy utilization ratio (0..1) across all Frame workers.</summary>
        public double FrameLaneUtilization => ComputeLaneUtilization(1, FrameWorkerCount);

        /// <summary>Gets the average busy utilization ratio (0..1) across all Background workers.</summary>
        public double BackgroundLaneUtilization => ComputeLaneUtilization(1 + FrameWorkerCount, BackgroundWorkerCount);

        /// <summary>Gets the ratio (0..1) of the window duration spent by the main thread waiting on jobs.</summary>
        public double MainThreadWaitRatio => WindowDurationMs > 0.0
            ? Math.Clamp(TotalMainWaitMs / WindowDurationMs, 0.0, 1.0)
            : 0.0;

        /// <summary>Resolves an interned job type or marker name from <see cref="JobProfileEvent.TypeId"/> or <see cref="JobTypeStats.TypeId"/>.</summary>
        public string GetTypeName(ushort typeId)
        {
            return typeId < (uint)_typeNames.Count ? _typeNames[typeId] : "(Unknown)";
        }

        /// <summary>Resolves a human-readable track label from <see cref="JobProfileEvent.TrackIndex"/> or <see cref="JobTrackStats.TrackIndex"/>.</summary>
        public string GetTrackName(int trackIndex)
        {
            return (uint)trackIndex < (uint)_trackNames.Length ? _trackNames[trackIndex] : $"Track {trackIndex}";
        }

        /// <summary>
        /// Captures diagnostics from <paramref name="scheduler"/> into this snapshot instance.
        /// Must be called from the scheduler's producer thread.
        /// </summary>
        /// <returns><see langword="true"/> when profiling was active and the capture succeeded; otherwise <see langword="false"/>.</returns>
        public bool TryCapture(JobScheduler scheduler)
        {
            ArgumentNullException.ThrowIfNull(scheduler);
            if (scheduler._disposed || !scheduler._profilingEnabled)
            {
                return false;
            }

            JobProfiler? profiler = scheduler._profiler;
            if (profiler == null)
            {
                return false;
            }

            profiler.CaptureInto(
                scheduler,
                _tracks,
                _events,
                _typeStats,
                _typeIndexScratch,
                _typeNames,
                out long startTimestamp,
                out long endTimestamp,
                out double durationMs,
                out int frameWorkerCount,
                out int backgroundWorkerCount,
                out JobSchedulerCounters counters);

            WindowStartTimestamp = startTimestamp;
            WindowEndTimestamp = endTimestamp;
            WindowDurationMs = durationMs;
            Counters = counters;
            EnsureTrackNames(frameWorkerCount, backgroundWorkerCount);
            return true;
        }

        /// <summary>
        /// Copies all captured fields, tracks, events, type statistics, and names from <paramref name="other"/> without allocating once buffers are sized.
        /// </summary>
        public void CopyFrom(ProfileSnapshot other)
        {
            ArgumentNullException.ThrowIfNull(other);
            if (ReferenceEquals(this, other))
            {
                return;
            }

            WindowStartTimestamp = other.WindowStartTimestamp;
            WindowEndTimestamp = other.WindowEndTimestamp;
            WindowDurationMs = other.WindowDurationMs;
            Counters = other.Counters;
            EnsureTrackNames(other.FrameWorkerCount, other.BackgroundWorkerCount);

            _tracks.Clear();
            for (int i = 0; i < other._tracks.Count; i++)
            {
                _tracks.Add(other._tracks[i]);
            }

            _events.Clear();
            for (int i = 0; i < other._events.Count; i++)
            {
                _events.Add(other._events[i]);
            }

            _typeStats.Clear();
            for (int i = 0; i < other._typeStats.Count; i++)
            {
                _typeStats.Add(other._typeStats[i]);
            }

            SyncTypeNames(other._typeNames, _typeNames);
        }

        /// <summary>
        /// Clears captured events, tracks, statistics, and timestamps.
        /// </summary>
        public void Clear()
        {
            WindowStartTimestamp = 0;
            WindowEndTimestamp = 0;
            WindowDurationMs = 0.0;
            FrameWorkerCount = 0;
            BackgroundWorkerCount = 0;
            Counters = default;
            _tracks.Clear();
            _events.Clear();
            _typeStats.Clear();
            _typeNames.Clear();
            _typeIndexScratch.Clear();
        }

        private static void SyncTypeNames(List<string> sourceNames, List<string> targetNames)
        {
            bool prefixMatches = targetNames.Count <= sourceNames.Count;
            if (prefixMatches)
            {
                for (int i = 0; i < targetNames.Count; i++)
                {
                    if (!ReferenceEquals(targetNames[i], sourceNames[i]))
                    {
                        prefixMatches = false;
                        break;
                    }
                }
            }

            if (!prefixMatches)
            {
                targetNames.Clear();
            }

            for (int i = targetNames.Count; i < sourceNames.Count; i++)
            {
                targetNames.Add(sourceNames[i]);
            }
        }

        private void EnsureTrackNames(int frameWorkerCount, int backgroundWorkerCount)
        {
            int totalTracks = 1 + frameWorkerCount + backgroundWorkerCount;
            if (FrameWorkerCount == frameWorkerCount &&
                BackgroundWorkerCount == backgroundWorkerCount &&
                _trackNames.Length == totalTracks)
            {
                return;
            }

            FrameWorkerCount = frameWorkerCount;
            BackgroundWorkerCount = backgroundWorkerCount;
            string[] names = new string[totalTracks];
            names[0] = "Main";
            for (int i = 0; i < frameWorkerCount; i++)
            {
                names[1 + i] = $"Frame {i}";
            }
            for (int i = 0; i < backgroundWorkerCount; i++)
            {
                names[1 + frameWorkerCount + i] = $"Bg {i}";
            }
            _trackNames = names;
        }

        private double ComputeLaneUtilization(int startIndex, int count)
        {
            if (count <= 0 || WindowDurationMs <= 0.0 || startIndex + count > _tracks.Count)
            {
                return 0.0;
            }

            double totalBusyMs = 0.0;
            for (int i = 0; i < count; i++)
            {
                totalBusyMs += _tracks[startIndex + i].BusyMs;
            }

            return Math.Clamp(totalBusyMs / (count * WindowDurationMs), 0.0, 1.0);
        }
    }

    private sealed class JobProfiler
    {
        private const int TrackRingCapacity = 2048;
        private const int TrackRingMask = TrackRingCapacity - 1;

        private const ushort UnknownTypeId = 0;
        private const ushort MainWaitSpinTypeId = 1;
        private const ushort MainWaitSleepTypeId = 2;
        private const ushort FallbackCallbackTypeId = 3;

        private static readonly Comparison<JobTypeStats> CompareTypeStatsByCpuDescending =
            static (left, right) =>
            {
                int cmp = right.TotalCpuMs.CompareTo(left.TotalCpuMs);
                return cmp != 0 ? cmp : left.TypeId.CompareTo(right.TypeId);
            };

        [StructLayout(LayoutKind.Explicit, Size = 256)]
        private struct PaddedTrackState
        {
            // Writer-exclusive cache line.
            [FieldOffset(64)] public long WriteIndex;
            [FieldOffset(72)] public long SpinTicksTotal;
            [FieldOffset(80)] public long ActiveStartTs;
            [FieldOffset(88)] public int ActiveMeta;

            // Reader-exclusive cache line.
            [FieldOffset(128)] public long ReadIndex;
            [FieldOffset(136)] public long LastSpinTicksRead;

            // Cross-thread wakeup counter cache line.
            [FieldOffset(192)] public int WakeCount;
        }

        private sealed class TrackBuffer
        {
            private readonly JobProfileEvent[] _ring = new JobProfileEvent[TrackRingCapacity];
            private PaddedTrackState _state;

            public void Reset()
            {
                _state.ReadIndex = Volatile.Read(ref _state.WriteIndex);
                _state.LastSpinTicksRead = Volatile.Read(ref _state.SpinTicksTotal);
                Volatile.Write(ref _state.ActiveStartTs, 0);
                Interlocked.Exchange(ref _state.WakeCount, 0);
            }

            public void IncrementWake()
            {
                Interlocked.Increment(ref _state.WakeCount);
            }

            public void AddSpinTicks(long deltaTicks)
            {
                Volatile.Write(ref _state.SpinTicksTotal, _state.SpinTicksTotal + deltaTicks);
            }

            public void BeginActive(
                long startTimestamp,
                ushort typeId,
                JobLane lane,
                JobProfileWorkKind workKind,
                JobProfileEventKind eventKind)
            {
                Volatile.Write(ref _state.ActiveMeta, PackMeta(typeId, lane, workKind, eventKind));
                Volatile.Write(ref _state.ActiveStartTs, startTimestamp);
            }

            public void EndActiveAndWrite(in JobProfileEvent ev)
            {
                // Clear active state before advancing WriteIndex so CaptureInto never double-counts.
                Volatile.Write(ref _state.ActiveStartTs, 0);
                WriteEvent(in ev);
            }

            public void WriteEvent(in JobProfileEvent ev)
            {
                long index = _state.WriteIndex;
                _ring[(int)(index & TrackRingMask)] = ev;
                Volatile.Write(ref _state.WriteIndex, index + 1);
            }

            public void Drain(
                long windowStart,
                long windowEnd,
                byte trackIndex,
                List<JobProfileEvent> destination,
                out long busyTicksClamped,
                out long mainSpinTicksClamped,
                out long mainSleepTicksClamped,
                out long workerSpinDeltaTicks,
                out int execs,
                out int chunks,
                out int items,
                out int wakeCount,
                out int dropped)
            {
                busyTicksClamped = 0;
                mainSpinTicksClamped = 0;
                mainSleepTicksClamped = 0;
                execs = 0;
                chunks = 0;
                items = 0;
                dropped = 0;

                long end = Volatile.Read(ref _state.WriteIndex);
                long start = _state.ReadIndex;
                long pending = end - start;
                if (pending > TrackRingCapacity)
                {
                    dropped += (int)(pending - TrackRingCapacity);
                    start = end - TrackRingCapacity;
                }

                long lastDrainedStartTs = 0;
                for (long i = start; i < end; i++)
                {
                    JobProfileEvent ev = _ring[(int)(i & TrackRingMask)];
                    if (Volatile.Read(ref _state.WriteIndex) - i > TrackRingCapacity)
                    {
                        dropped++;
                        continue;
                    }

                    destination.Add(ev);
                    lastDrainedStartTs = ev.StartTimestamp;

                    long clampedStart = Math.Max(ev.StartTimestamp, windowStart);
                    long clampedEnd = Math.Min(ev.EndTimestamp, windowEnd);
                    long clampedTicks = clampedEnd > clampedStart ? clampedEnd - clampedStart : 0;

                    switch (ev.Kind)
                    {
                        case JobProfileEventKind.JobExecute:
                        case JobProfileEventKind.MainHelp:
                            busyTicksClamped += clampedTicks;
                            execs++;
                            chunks += ev.ChunksProcessed;
                            items += ev.ItemsProcessed;
                            break;
                        case JobProfileEventKind.MainWaitSpin:
                            mainSpinTicksClamped += clampedTicks;
                            break;
                        case JobProfileEventKind.MainWaitSleep:
                            mainSleepTicksClamped += clampedTicks;
                            break;
                    }
                }

                _state.ReadIndex = end;

                long spinTotal = Volatile.Read(ref _state.SpinTicksTotal);
                workerSpinDeltaTicks = Math.Max(0, spinTotal - _state.LastSpinTicksRead);
                _state.LastSpinTicksRead = spinTotal;

                wakeCount = Interlocked.Exchange(ref _state.WakeCount, 0);

                if (trackIndex > 0)
                {
                    long activeStart = Volatile.Read(ref _state.ActiveStartTs);
                    int activeMeta = Volatile.Read(ref _state.ActiveMeta);
                    if (activeStart != 0 &&
                        activeStart < windowEnd &&
                        activeStart != lastDrainedStartTs &&
                        Volatile.Read(ref _state.ActiveStartTs) == activeStart)
                    {
                        UnpackMeta(
                            activeMeta,
                            out ushort typeId,
                            out JobLane lane,
                            out JobProfileWorkKind workKind,
                            out JobProfileEventKind eventKind);

                        var inProgress = new JobProfileEvent(
                            activeStart,
                            windowEnd,
                            itemsProcessed: 0,
                            chunksProcessed: 0,
                            typeId,
                            trackIndex,
                            eventKind,
                            lane,
                            workKind,
                            JobProfileEvent.FlagInProgress);

                        destination.Add(inProgress);

                        long clampedStart = Math.Max(activeStart, windowStart);
                        if (windowEnd > clampedStart)
                        {
                            busyTicksClamped += windowEnd - clampedStart;
                        }
                        execs++;
                    }
                }
            }

            private static int PackMeta(
                ushort typeId,
                JobLane lane,
                JobProfileWorkKind workKind,
                JobProfileEventKind eventKind)
            {
                return typeId | ((int)lane << 16) | ((int)workKind << 18) | ((int)eventKind << 22);
            }

            private static void UnpackMeta(
                int packed,
                out ushort typeId,
                out JobLane lane,
                out JobProfileWorkKind workKind,
                out JobProfileEventKind eventKind)
            {
                typeId = (ushort)(packed & 0xFFFF);
                lane = (JobLane)((packed >> 16) & 0x3);
                workKind = (JobProfileWorkKind)((packed >> 18) & 0xF);
                eventKind = (JobProfileEventKind)((packed >> 22) & 0xF);
            }
        }

        private readonly int _frameWorkerCount;
        private readonly int _backgroundWorkerCount;
        private readonly TrackBuffer[] _tracks;
        private readonly Dictionary<Type, ushort> _jobTypeIds = new();
        private readonly Dictionary<string, ushort> _namedIds = new(StringComparer.Ordinal);
        private readonly Dictionary<MethodInfo, ushort> _callbackIds = new();
        private readonly List<string> _typeNames = new();

        private long _windowStartTimestamp;
        private int _jobsScheduledFrame;
        private int _jobsScheduledBackground;
        private int _fencesCreated;
        private int _peakActiveSlots;

        public JobProfiler(int frameWorkerCount, int backgroundWorkerCount)
        {
            _frameWorkerCount = frameWorkerCount;
            _backgroundWorkerCount = backgroundWorkerCount;

            int trackCount = 1 + frameWorkerCount + backgroundWorkerCount;
            _tracks = new TrackBuffer[trackCount];
            for (int i = 0; i < trackCount; i++)
            {
                _tracks[i] = new TrackBuffer();
            }

            _typeNames.Add("(Unknown)");
            _typeNames.Add("MainWait (Spin)");
            _typeNames.Add("MainWait (Sleep)");
            _typeNames.Add("Callback");
        }

        public void Reset(long nowTimestamp, int activeSlotCount)
        {
            for (int i = 0; i < _tracks.Length; i++)
            {
                _tracks[i].Reset();
            }

            _windowStartTimestamp = nowTimestamp;
            _jobsScheduledFrame = 0;
            _jobsScheduledBackground = 0;
            _fencesCreated = 0;
            _peakActiveSlots = activeSlotCount;
        }

        public ushort RegisterJobType(Type type)
        {
            if (_jobTypeIds.TryGetValue(type, out ushort id))
            {
                return id;
            }

            if (_typeNames.Count >= ushort.MaxValue)
            {
                return UnknownTypeId;
            }

            string name = type.DeclaringType != null
                ? $"{type.DeclaringType.Name}.{type.Name}"
                : type.Name;
            id = (ushort)_typeNames.Count;
            _typeNames.Add(name);
            _jobTypeIds.Add(type, id);
            return id;
        }

        public ushort RegisterCallback(JobDelegate callback)
        {
            MethodInfo method = callback.Method;
            if (_callbackIds.TryGetValue(method, out ushort id))
            {
                return id;
            }

            if (_typeNames.Count >= ushort.MaxValue)
            {
                return FallbackCallbackTypeId;
            }

            string name = method.DeclaringType != null
                ? $"{method.DeclaringType.Name}.{method.Name}"
                : method.Name;
            id = (ushort)_typeNames.Count;
            _typeNames.Add(name);
            _callbackIds.Add(method, id);
            return id;
        }

        public ushort RegisterMarkerName(string name)
        {
            if (_namedIds.TryGetValue(name, out ushort id))
            {
                return id;
            }

            if (_typeNames.Count >= ushort.MaxValue)
            {
                return UnknownTypeId;
            }

            id = (ushort)_typeNames.Count;
            _typeNames.Add(name);
            _namedIds.Add(name, id);
            return id;
        }

        public void OnJobScheduled(JobLane lane, int activeSlotCount)
        {
            if (lane == JobLane.Frame)
            {
                _jobsScheduledFrame++;
            }
            else
            {
                _jobsScheduledBackground++;
            }

            if (activeSlotCount > _peakActiveSlots)
            {
                _peakActiveSlots = activeSlotCount;
            }
        }

        public void OnFenceCreated(int activeSlotCount)
        {
            _fencesCreated++;
            if (activeSlotCount > _peakActiveSlots)
            {
                _peakActiveSlots = activeSlotCount;
            }
        }

        public void OnWorkerWoken(bool isFrameLane, int workerIndex)
        {
            int trackIndex = isFrameLane ? (1 + workerIndex) : (1 + _frameWorkerCount + workerIndex);
            if ((uint)trackIndex < (uint)_tracks.Length)
            {
                _tracks[trackIndex].IncrementWake();
            }
        }

        public void AddTrackSpinTicks(int trackIndex, long deltaTicks)
        {
            if ((uint)trackIndex < (uint)_tracks.Length && deltaTicks > 0)
            {
                _tracks[trackIndex].AddSpinTicks(deltaTicks);
            }
        }

        public void BeginActiveSpan(
            int trackIndex,
            long startTimestamp,
            ushort typeId,
            JobLane lane,
            JobProfileWorkKind workKind,
            JobProfileEventKind eventKind)
        {
            if ((uint)trackIndex < (uint)_tracks.Length)
            {
                _tracks[trackIndex].BeginActive(startTimestamp, typeId, lane, workKind, eventKind);
            }
        }

        public void EndActiveSpan(
            int trackIndex,
            long startTimestamp,
            long endTimestamp,
            ushort typeId,
            JobLane lane,
            JobProfileWorkKind workKind,
            JobProfileEventKind eventKind,
            int itemsProcessed,
            int chunksProcessed,
            bool failed)
        {
            if ((uint)trackIndex >= (uint)_tracks.Length)
            {
                return;
            }

            byte flags = failed ? JobProfileEvent.FlagFailed : (byte)0;
            var ev = new JobProfileEvent(
                startTimestamp,
                endTimestamp,
                itemsProcessed,
                chunksProcessed,
                typeId,
                (byte)trackIndex,
                eventKind,
                lane,
                workKind,
                flags);
            _tracks[trackIndex].EndActiveAndWrite(in ev);
        }

        public void RecordMainWait(JobProfileEventKind waitKind, long startTimestamp, long endTimestamp)
        {
            if (endTimestamp <= startTimestamp)
            {
                return;
            }

            ushort typeId = waitKind == JobProfileEventKind.MainWaitSpin ? MainWaitSpinTypeId : MainWaitSleepTypeId;
            var ev = new JobProfileEvent(
                startTimestamp,
                endTimestamp,
                itemsProcessed: 0,
                chunksProcessed: 0,
                typeId,
                trackIndex: 0,
                waitKind,
                JobLane.Frame,
                JobProfileWorkKind.None,
                flags: 0);
            _tracks[0].WriteEvent(in ev);
        }

        public void RecordMarker(long timestamp, ushort typeId)
        {
            var ev = new JobProfileEvent(
                timestamp,
                timestamp,
                itemsProcessed: 0,
                chunksProcessed: 0,
                typeId,
                trackIndex: 0,
                JobProfileEventKind.Marker,
                JobLane.Frame,
                JobProfileWorkKind.None,
                flags: 0);
            _tracks[0].WriteEvent(in ev);
        }

        public void CaptureInto(
            JobScheduler scheduler,
            List<JobTrackStats> tracks,
            List<JobProfileEvent> events,
            List<JobTypeStats> typeStats,
            Dictionary<(ushort TypeId, byte Lane, byte WorkKind), int> typeIndexScratch,
            List<string> targetTypeNames,
            out long windowStart,
            out long windowEnd,
            out double windowDurationMs,
            out int frameWorkerCount,
            out int backgroundWorkerCount,
            out JobSchedulerCounters counters)
        {
            windowEnd = Stopwatch.GetTimestamp();
            windowStart = _windowStartTimestamp == 0 ? windowEnd : _windowStartTimestamp;
            _windowStartTimestamp = windowEnd;

            double ticksToMs = 1000.0 / Stopwatch.Frequency;
            windowDurationMs = windowEnd > windowStart ? (windowEnd - windowStart) * ticksToMs : 0.0;
            frameWorkerCount = _frameWorkerCount;
            backgroundWorkerCount = _backgroundWorkerCount;

            tracks.Clear();
            events.Clear();
            typeStats.Clear();
            typeIndexScratch.Clear();

            int frameWakeups = 0;
            int backgroundWakeups = 0;
            int totalDropped = 0;

            for (int i = 0; i < _tracks.Length; i++)
            {
                _tracks[i].Drain(
                    windowStart,
                    windowEnd,
                    (byte)i,
                    events,
                    out long busyTicksClamped,
                    out long mainSpinTicksClamped,
                    out long mainSleepTicksClamped,
                    out long workerSpinDeltaTicks,
                    out int execs,
                    out int chunks,
                    out int items,
                    out int wakeCount,
                    out int dropped);

                totalDropped += dropped;

                if (i == 0)
                {
                    tracks.Add(new JobTrackStats(
                        trackIndex: 0,
                        JobLane.Frame,
                        isMainThread: true,
                        busyMs: busyTicksClamped * ticksToMs,
                        spinMs: mainSpinTicksClamped * ticksToMs,
                        sleepMs: mainSleepTicksClamped * ticksToMs,
                        execs,
                        chunks,
                        items,
                        wakeCount: 0));
                }
                else
                {
                    JobLane lane = i <= _frameWorkerCount ? JobLane.Frame : JobLane.Background;
                    if (lane == JobLane.Frame)
                    {
                        frameWakeups += wakeCount;
                    }
                    else
                    {
                        backgroundWakeups += wakeCount;
                    }

                    double busyMs = Math.Min(windowDurationMs, busyTicksClamped * ticksToMs);
                    double remainingMs = Math.Max(0.0, windowDurationMs - busyMs);
                    double spinMs = Math.Min(remainingMs, workerSpinDeltaTicks * ticksToMs);
                    double sleepMs = Math.Max(0.0, remainingMs - spinMs);

                    tracks.Add(new JobTrackStats(
                        (byte)i,
                        lane,
                        isMainThread: false,
                        busyMs,
                        spinMs,
                        sleepMs,
                        execs,
                        chunks,
                        items,
                        wakeCount));
                }
            }

            for (int i = 0; i < events.Count; i++)
            {
                JobProfileEvent ev = events[i];
                if (ev.Kind != JobProfileEventKind.JobExecute && ev.Kind != JobProfileEventKind.MainHelp)
                {
                    continue;
                }

                var key = (ev.TypeId, (byte)ev.Lane, (byte)ev.WorkKind);
                double durationMs = ev.DurationMs;
                if (typeIndexScratch.TryGetValue(key, out int existingIndex))
                {
                    JobTypeStats existing = typeStats[existingIndex];
                    typeStats[existingIndex] = new JobTypeStats(
                        existing.TypeId,
                        existing.Lane,
                        existing.WorkKind,
                        existing.Executions + 1,
                        existing.Chunks + ev.ChunksProcessed,
                        existing.Items + ev.ItemsProcessed,
                        existing.TotalCpuMs + durationMs,
                        Math.Max(existing.MaxSingleMs, durationMs));
                }
                else
                {
                    typeIndexScratch.Add(key, typeStats.Count);
                    typeStats.Add(new JobTypeStats(
                        ev.TypeId,
                        ev.Lane,
                        ev.WorkKind,
                        executions: 1,
                        ev.ChunksProcessed,
                        ev.ItemsProcessed,
                        durationMs,
                        durationMs));
                }
            }

            typeStats.Sort(CompareTypeStatsByCpuDescending);

            int activeSlots = scheduler._activeSlotCount;
            int peakActiveSlots = Math.Max(_peakActiveSlots, activeSlots);
            int usedEdges = scheduler._edges.Length - scheduler._freeEdgeCount;

            counters = new JobSchedulerCounters(
                slotCapacity: scheduler._hot.Length,
                activeSlots: activeSlots,
                peakActiveSlots: peakActiveSlots,
                edgeCapacity: scheduler._edges.Length,
                usedEdges: usedEdges,
                frameQueueDepth: scheduler._frameLane.ApproximateQueueDepth,
                frameQueueCapacity: scheduler._frameLane.QueueCapacity,
                backgroundQueueDepth: scheduler._backgroundLane.ApproximateQueueDepth,
                backgroundQueueCapacity: scheduler._backgroundLane.QueueCapacity,
                activeFrameJobs: Volatile.Read(ref scheduler._activeFrameJobs.Value),
                activeBackgroundJobs: Volatile.Read(ref scheduler._activeBackgroundJobs.Value),
                frameWorkersSleeping: scheduler._frameLane.SleepingWorkerCount,
                backgroundWorkersSleeping: scheduler._backgroundLane.SleepingWorkerCount,
                jobsScheduledFrame: _jobsScheduledFrame,
                jobsScheduledBackground: _jobsScheduledBackground,
                fencesCreated: _fencesCreated,
                frameWakeups: frameWakeups,
                backgroundWakeups: backgroundWakeups,
                droppedEvents: totalDropped);

            SyncNamesInto(targetTypeNames);

            _peakActiveSlots = activeSlots;
            _jobsScheduledFrame = 0;
            _jobsScheduledBackground = 0;
            _fencesCreated = 0;
        }

        private void SyncNamesInto(List<string> targetTypeNames)
        {
            bool prefixMatches = targetTypeNames.Count <= _typeNames.Count;
            if (prefixMatches)
            {
                for (int i = 0; i < targetTypeNames.Count; i++)
                {
                    if (!ReferenceEquals(targetTypeNames[i], _typeNames[i]))
                    {
                        prefixMatches = false;
                        break;
                    }
                }
            }

            if (!prefixMatches)
            {
                targetTypeNames.Clear();
            }

            for (int i = targetTypeNames.Count; i < _typeNames.Count; i++)
            {
                targetTypeNames.Add(_typeNames[i]);
            }
        }
    }
}
