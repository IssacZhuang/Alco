namespace Alco.Profiler;

[Flags]
internal enum TraceScope : byte
{
    All = 0,
    Tick = 1,
    Update = 2,
    GameLoop = 4,
}

internal enum TraceSliceMode : byte
{
    Overview,
    HalfSecond,
    Second,
    Tick,
    Update,
}

/// <summary>A half-open selection on the capture timeline, in milliseconds.</summary>
internal readonly record struct TraceRange(double StartMs, double EndMs);

/// <summary>A heatmap column's exact time range and sequence number.</summary>
internal readonly record struct TraceSlice(TraceRange Range, long Sequence);

/// <summary>A page of slices for the hottest sampled leaf methods.</summary>
internal sealed record HeatmapData(TraceSliceMode Mode, IReadOnlyList<TraceSlice> Slices, IReadOnlyList<HeatmapRow> Rows)
{
    /// <summary>Gets the number of slices on this page.</summary>
    public int ColumnCount => Slices.Count;

    /// <summary>Gets whether each slice represents a recorded invocation.</summary>
    public bool IsPerInvocation => Mode is TraceSliceMode.Tick or TraceSliceMode.Update;
}

/// <summary>The leaf sample counts of one method across the timeline.</summary>
internal sealed record HeatmapRow(string Method, int[] Samples);

/// <summary>A sampled stack path and the paths that extend it.</summary>
internal sealed record CallNode(string Method, int TotalSamples, int SelfSamples, IReadOnlyList<CallNode> Children);

/// <summary>A thread filter and its display label.</summary>
internal sealed record ThreadChoice(int? Id, string Label, bool IsGameLoop = false);

/// <summary>Sample counts and estimated times for one method across all selected paths.</summary>
internal sealed record MethodSummary(string Method, int TotalSamples, int SelfSamples, double? TotalMs, double? SelfMs)
{
    /// <summary>Shows inclusive and self sample counts.</summary>
    public string SampleCounts => $"{TotalSamples:N0} / {SelfSamples:N0}";

    /// <summary>Ranks methods by inclusive or self samples, highest first.</summary>
    public static IOrderedEnumerable<MethodSummary> OrderByCost(IEnumerable<MethodSummary> methods, bool sortBySelf)
    {
        var ordered = sortBySelf
            ? methods.OrderByDescending(method => method.SelfSamples).ThenByDescending(method => method.TotalSamples)
            : methods.OrderByDescending(method => method.TotalSamples).ThenByDescending(method => method.SelfSamples);
        return ordered.ThenBy(method => method.Method, StringComparer.Ordinal);
    }
}

/// <summary>Aggregated paths and methods for a selected set of samples.</summary>
internal sealed record ProfileAnalysis(CallNode? Root, MethodSummary[] Methods, int SampleCount);

internal static class MethodNameUtility
{
    /// <summary>Returns the type and method label used in compact charts.</summary>
    public static string ShortName(string method)
    {
        if (method.StartsWith('['))
        {
            return method;
        }

        int end = method.IndexOf('(');
        if (end < 0)
        {
            end = method.IndexOf("  [", StringComparison.Ordinal);
        }
        if (end < 0)
        {
            end = method.Length;
        }

        ReadOnlySpan<char> name = method.AsSpan(0, end);
        int typeEnd = name.LastIndexOf('.');
        if (typeEnd > 0 && name[typeEnd - 1] == '.')
        {
            typeEnd--;
        }
        int start = typeEnd > 0 ? name[..typeEnd].LastIndexOf('.') + 1 : 0;
        return method[start..end];
    }
}

internal sealed partial class TraceProfile
{
    private const int HeatmapRowCount = 6;
    private const int MaxHeatmapColumns = 120;

    private readonly record struct Sample(double TimeMs, int ThreadId, int Stack);
    private readonly record struct StackInfo(int Method, int Parent, TraceScope Scope, bool RepeatedMethod);

    private readonly string[] _methods;
    private readonly StackInfo[] _stacks;
    private readonly Sample[] _samples;
    private readonly TraceSlice[] _ticks;
    private readonly TraceSlice[] _updates;

    private TraceProfile(string[] methods, StackInfo[] stacks, Sample[] samples, IReadOnlyList<ThreadChoice> threads,
        TraceSlice[] ticks, TraceSlice[] updates, double? sampleIntervalMs, int lostEvents)
    {
        _methods = methods;
        _stacks = stacks;
        _samples = samples;
        _ticks = ticks;
        _updates = updates;
        Threads = threads;
        double sampleEnd = samples.Length == 0 ? 0 : Math.BitIncrement(samples[^1].TimeMs);
        double tickEnd = ticks.Length == 0 ? 0 : ticks.Max(static slice => slice.Range.EndMs);
        double updateEnd = updates.Length == 0 ? 0 : updates.Max(static slice => slice.Range.EndMs);
        DurationMs = Math.Max(1, Math.Max(sampleEnd, Math.Max(tickEnd, updateEnd)));
        SampleIntervalMs = sampleIntervalMs;
        LostEvents = lostEvents;
    }

    /// <summary>Gets the end of the capture timeline in milliseconds.</summary>
    public double DurationMs { get; }

    /// <summary>Gets the number of retained stack samples.</summary>
    public int SampleCount => _samples.Length;

    /// <summary>Gets the observed sampling interval used for time estimates, if known.</summary>
    public double? SampleIntervalMs { get; }

    /// <summary>Gets the number of events reported lost in the trace.</summary>
    public int LostEvents { get; }

    /// <summary>Gets selectable threads from the trace.</summary>
    public IReadOnlyList<ThreadChoice> Threads { get; }

    /// <summary>Gets the number of slices in a mode, or zero when it is unavailable.</summary>
    public int GetSliceCount(TraceSliceMode mode) => mode switch
    {
        TraceSliceMode.HalfSecond => (int)Math.Ceiling(DurationMs / 500),
        TraceSliceMode.Second => (int)Math.Ceiling(DurationMs / 1000),
        TraceSliceMode.Tick => _ticks.Length,
        TraceSliceMode.Update => _updates.Length,
        _ => (int)Math.Clamp(Math.Ceiling(DurationMs / 20), 1, MaxHeatmapColumns),
    };

    /// <summary>Gets the number of heatmap pages in a slice mode.</summary>
    public int GetPageCount(TraceSliceMode mode)
    {
        int slices = GetSliceCount(mode);
        return slices == 0 ? 0 : (slices - 1) / MaxHeatmapColumns + 1;
    }

    /// <summary>Builds a page of the heatmap for the selected thread, stack scope and slice mode.</summary>
    public HeatmapData BuildHeatmap(int? threadId, TraceScope scope, TraceSliceMode mode, int page, CancellationToken token)
    {
        TraceSlice[] slices = GetPageSlices(mode, page);
        var bounds = new (int From, int To)[slices.Length];
        var methodCounts = new int[_methods.Length];
        for (int column = 0; column < slices.Length; column++)
        {
            token.ThrowIfCancellationRequested();
            TraceRange range = slices[column].Range;
            int from = FindSampleAtOrAfter(range.StartMs);
            int to = FindSampleAtOrAfter(range.EndMs);
            bounds[column] = (from, to);
            for (int i = from; i < to; i++)
            {
                CheckCancellation(i, token);
                Sample sample = _samples[i];
                if (Matches(sample, threadId, scope))
                {
                    methodCounts[_stacks[sample.Stack].Method]++;
                }
            }
        }

        int[] hottest = Enumerable.Range(0, _methods.Length)
            .Where(i => methodCounts[i] > 0)
            .OrderByDescending(i => methodCounts[i])
            .Take(HeatmapRowCount)
            .ToArray();
        var rows = new HeatmapRow[hottest.Length];
        var binsByMethod = new Dictionary<int, int[]>(hottest.Length);
        for (int i = 0; i < hottest.Length; i++)
        {
            int method = hottest[i];
            int[] bins = new int[slices.Length];
            binsByMethod.Add(method, bins);
            rows[i] = new HeatmapRow(_methods[method], bins);
        }

        for (int column = 0; column < slices.Length && rows.Length != 0; column++)
        {
            var (from, to) = bounds[column];
            for (int i = from; i < to; i++)
            {
                CheckCancellation(i, token);
                Sample sample = _samples[i];
                if (Matches(sample, threadId, scope) && binsByMethod.TryGetValue(_stacks[sample.Stack].Method, out int[]? bins))
                {
                    bins[column]++;
                }
            }
        }

        return new HeatmapData(mode, slices, rows);
    }

    private TraceSlice[] GetPageSlices(TraceSliceMode mode, int page)
    {
        int total = GetSliceCount(mode);
        int offset = page * MaxHeatmapColumns;
        int count = Math.Min(total - offset, MaxHeatmapColumns);
        if (count <= 0)
        {
            return [];
        }

        if (mode is TraceSliceMode.Tick or TraceSliceMode.Update)
        {
            TraceSlice[] invocations = mode == TraceSliceMode.Tick ? _ticks : _updates;
            return invocations.AsSpan(offset, count).ToArray();
        }

        double width = mode switch
        {
            TraceSliceMode.HalfSecond => 500,
            TraceSliceMode.Second => 1000,
            _ => DurationMs / total,
        };
        var slices = new TraceSlice[count];
        for (int i = 0; i < count; i++)
        {
            int index = offset + i;
            double end = index + 1 == total ? DurationMs : (index + 1) * width;
            slices[i] = new TraceSlice(new TraceRange(index * width, end), index + 1);
        }

        return slices;
    }

    /// <summary>Aggregates samples in the selection into call paths and method totals.</summary>
    public ProfileAnalysis Analyze(int? threadId, TraceScope scope, TraceRange? range, CancellationToken token)
    {
        int from = range.HasValue ? FindSampleAtOrAfter(range.Value.StartMs) : 0;
        int to = range.HasValue ? FindSampleAtOrAfter(range.Value.EndMs) : _samples.Length;
        int[] selfSamples = new int[_stacks.Length];
        int sampleCount = 0;
        for (int i = from; i < to; i++)
        {
            CheckCancellation(i, token);
            Sample sample = _samples[i];
            if (Matches(sample, threadId, scope))
            {
                selfSamples[sample.Stack]++;
                sampleCount++;
            }
        }

        if (sampleCount == 0)
        {
            return new ProfileAnalysis(null, [], 0);
        }

        var roots = new List<CallNode>();
        var children = new List<CallNode>?[_stacks.Length];
        int[] totalSamples = (int[])selfSamples.Clone();
        int[] methodTotals = new int[_methods.Length];
        int[] methodSelf = new int[_methods.Length];
        for (int i = _stacks.Length - 1; i >= 0; i--)
        {
            CheckCancellation(i, token);
            int total = totalSamples[i];
            if (total == 0)
            {
                continue;
            }

            StackInfo stack = _stacks[i];
            int self = selfSamples[i];
            methodSelf[stack.Method] += self;
            if (!stack.RepeatedMethod)
            {
                methodTotals[stack.Method] += total;
            }

            List<CallNode>? branch = children[i];
            branch?.Sort(CompareBySamples);
            var node = new CallNode(_methods[stack.Method], total, self, branch is null ? [] : branch);
            if (stack.Parent < 0)
            {
                roots.Add(node);
                continue;
            }

            totalSamples[stack.Parent] += total;
            (children[stack.Parent] ??= []).Add(node);
        }

        roots.Sort(CompareBySamples);
        var summary = new List<MethodSummary>();
        for (int i = 0; i < _methods.Length; i++)
        {
            if (methodTotals[i] > 0)
            {
                summary.Add(new MethodSummary(_methods[i], methodTotals[i], methodSelf[i],
                    methodTotals[i] * SampleIntervalMs, methodSelf[i] * SampleIntervalMs));
            }
        }

        return new ProfileAnalysis(new CallNode("Selected samples", sampleCount, 0, roots), summary.ToArray(), sampleCount);
    }

    private int FindSampleAtOrAfter(double timeMs)
    {
        int lo = 0;
        int hi = _samples.Length;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (_samples[mid].TimeMs < timeMs)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }

    private bool Matches(Sample sample, int? threadId, TraceScope scope)
    {
        return (!threadId.HasValue || sample.ThreadId == threadId.Value)
            && (scope == TraceScope.All || (_stacks[sample.Stack].Scope & scope) != 0);
    }

    private static void CheckCancellation(int index, CancellationToken token)
    {
        if ((index & 4095) == 0)
        {
            token.ThrowIfCancellationRequested();
        }
    }

    private static int CompareBySamples(CallNode x, CallNode y)
    {
        int result = y.TotalSamples.CompareTo(x.TotalSamples);
        return result != 0 ? result : StringComparer.Ordinal.Compare(x.Method, y.Method);
    }
}
