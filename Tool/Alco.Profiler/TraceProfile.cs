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

internal enum TraceMetric : byte
{
    Time,
    Allocation,
}

/// <summary>A half-open selection on the capture timeline, in milliseconds.</summary>
internal readonly record struct TraceRange(double StartMs, double EndMs);

/// <summary>A heatmap column's exact time range and sequence number.</summary>
internal readonly record struct TraceSlice(TraceRange Range, long Sequence);

/// <summary>A count of recorded samples and their estimated allocation bytes, if applicable.</summary>
internal readonly record struct TraceCost(int Samples, double EstimatedBytes = 0)
{
    /// <summary>Gets the chart and ranking weight for this sample source.</summary>
    public double Weight => EstimatedBytes > 0 ? EstimatedBytes : Samples;

    /// <summary>Combines costs from the same sample source.</summary>
    public static TraceCost operator +(TraceCost left, TraceCost right)
        => new(left.Samples + right.Samples, left.EstimatedBytes + right.EstimatedBytes);

    /// <summary>Formats a byte count using binary units.</summary>
    public static string FormatBytes(double bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824:N2} GiB",
        >= 1_048_576 => $"{bytes / 1_048_576:N2} MiB",
        >= 1_024 => $"{bytes / 1_024:N2} KiB",
        _ => $"{bytes:N0} B",
    };
}

/// <summary>A page of slices for the hottest sampled leaf methods.</summary>
internal sealed record HeatmapData(TraceMetric Metric, TraceSliceMode Mode, IReadOnlyList<TraceSlice> Slices, IReadOnlyList<HeatmapRow> Rows)
{
    /// <summary>Gets the number of slices on this page.</summary>
    public int ColumnCount => Slices.Count;

    /// <summary>Gets whether each slice represents a recorded invocation.</summary>
    public bool IsPerInvocation => Mode is TraceSliceMode.Tick or TraceSliceMode.Update;
}

/// <summary>The leaf costs of one method across the timeline.</summary>
internal sealed record HeatmapRow(string Method, TraceCost[] Costs);

/// <summary>A sampled stack path and the paths that extend it.</summary>
internal sealed record CallNode(string Method, TraceCost Total, TraceCost Self, IReadOnlyList<CallNode> Children)
{
    /// <summary>Shows the inclusive cost in the units of the sampled data.</summary>
    public string TotalLabel => Total.EstimatedBytes > 0 ? $"≈ {TraceCost.FormatBytes(Total.EstimatedBytes)}" : $"{Total.Samples:N0} samples";
}

/// <summary>A thread filter and its display label.</summary>
internal sealed record ThreadChoice(int? Id, string Label, bool IsGameLoop = false);

/// <summary>Sample counts and estimated costs for one method across all selected paths.</summary>
internal sealed record MethodSummary(string Method, TraceCost Total, TraceCost Self, double? SampleIntervalMs)
{
    /// <summary>Gets the estimated inclusive time when analyzing thread stacks.</summary>
    public double? TotalMs => Total.Samples * SampleIntervalMs;

    /// <summary>Gets the estimated self time when analyzing thread stacks.</summary>
    public double? SelfMs => Self.Samples * SampleIntervalMs;

    /// <summary>Shows the inclusive estimate in the units of the sampled data.</summary>
    public string TotalLabel => FormatCost(Total, TotalMs);

    /// <summary>Shows the self estimate in the units of the sampled data.</summary>
    public string SelfLabel => FormatCost(Self, SelfMs);

    /// <summary>Shows inclusive and self sample counts.</summary>
    public string SampleCounts => $"{Total.Samples:N0} / {Self.Samples:N0}";

    /// <summary>Ranks methods by inclusive or self cost, highest first.</summary>
    public static IOrderedEnumerable<MethodSummary> OrderByCost(IEnumerable<MethodSummary> methods, bool sortBySelf)
    {
        var ordered = sortBySelf
            ? methods.OrderByDescending(method => method.Self.Weight).ThenByDescending(method => method.Total.Weight)
            : methods.OrderByDescending(method => method.Total.Weight).ThenByDescending(method => method.Self.Weight);
        return ordered.ThenBy(method => method.Method, StringComparer.Ordinal);
    }

    private string FormatCost(TraceCost cost, double? milliseconds)
        => Total.EstimatedBytes > 0 ? TraceCost.FormatBytes(cost.EstimatedBytes) : milliseconds?.ToString("N2") ?? "—";
}

/// <summary>The estimated allocated bytes and sample count for one managed object type.</summary>
internal sealed record AllocationTypeSummary(string TypeName, TraceCost Cost)
{
    /// <summary>Shows the estimated size of allocations of this type.</summary>
    public string CostLabel => TraceCost.FormatBytes(Cost.EstimatedBytes);
}

/// <summary>Aggregated calls, methods and types for a selected range and sample source.</summary>
internal sealed record ProfileAnalysis(CallNode? Root, MethodSummary[] Methods, AllocationTypeSummary[] Types, TraceCost Total);

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
    private readonly record struct AllocationWeight(int Type, double EstimatedBytes);
    private sealed record AllocationData(Sample[] Samples, AllocationWeight[] Weights, string[] Types);

    private readonly string[] _methods;
    private readonly StackInfo[] _stacks;
    private readonly Sample[] _samples;
    private readonly AllocationData? _allocations;
    private readonly TraceSlice[] _ticks;
    private readonly TraceSlice[] _updates;

    private TraceProfile(string[] methods, StackInfo[] stacks, Sample[] samples, AllocationData? allocations,
        IReadOnlyList<ThreadChoice> threads, TraceSlice[] ticks, TraceSlice[] updates, double? sampleIntervalMs, int lostEvents)
    {
        _methods = methods;
        _stacks = stacks;
        _samples = samples;
        _allocations = allocations;
        _ticks = ticks;
        _updates = updates;
        Threads = threads;
        double sampleEnd = samples.Length == 0 ? 0 : Math.BitIncrement(samples[^1].TimeMs);
        double allocationEnd = allocations == null ? 0 : Math.BitIncrement(allocations.Samples[^1].TimeMs);
        double tickEnd = ticks.Length == 0 ? 0 : ticks.Max(static slice => slice.Range.EndMs);
        double updateEnd = updates.Length == 0 ? 0 : updates.Max(static slice => slice.Range.EndMs);
        DurationMs = Math.Max(1, Math.Max(Math.Max(sampleEnd, allocationEnd), Math.Max(tickEnd, updateEnd)));
        SampleIntervalMs = sampleIntervalMs;
        LostEvents = lostEvents;
    }

    /// <summary>Gets the end of the capture timeline in milliseconds.</summary>
    public double DurationMs { get; }

    /// <summary>Gets the number of retained stack samples.</summary>
    public int SampleCount => _samples.Length;

    /// <summary>Gets the number of retained allocation samples.</summary>
    public int AllocationSampleCount => _allocations?.Samples.Length ?? 0;

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
    public HeatmapData BuildHeatmap(TraceMetric metric, int? threadId, TraceScope scope, TraceSliceMode mode, int page, CancellationToken token)
    {
        TraceSlice[] slices = GetPageSlices(mode, page);
        Sample[] samples = GetSamples(metric);
        AllocationWeight[]? weights = GetAllocationWeights(metric);
        var bounds = new (int From, int To)[slices.Length];
        var methodWeights = new double[_methods.Length];
        for (int column = 0; column < slices.Length; column++)
        {
            token.ThrowIfCancellationRequested();
            TraceRange range = slices[column].Range;
            int from = FindSampleAtOrAfter(samples, range.StartMs);
            int to = FindSampleAtOrAfter(samples, range.EndMs);
            bounds[column] = (from, to);
            for (int i = from; i < to; i++)
            {
                CheckCancellation(i, token);
                Sample sample = samples[i];
                if (Matches(sample, threadId, scope))
                {
                    methodWeights[_stacks[sample.Stack].Method] += weights?[i].EstimatedBytes ?? 1;
                }
            }
        }

        int[] hottest = Enumerable.Range(0, _methods.Length)
            .Where(i => methodWeights[i] > 0)
            .OrderByDescending(i => methodWeights[i])
            .Take(HeatmapRowCount)
            .ToArray();
        var rows = new HeatmapRow[hottest.Length];
        var binsByMethod = new Dictionary<int, TraceCost[]>(hottest.Length);
        for (int i = 0; i < hottest.Length; i++)
        {
            int method = hottest[i];
            var bins = new TraceCost[slices.Length];
            binsByMethod.Add(method, bins);
            rows[i] = new HeatmapRow(_methods[method], bins);
        }

        for (int column = 0; column < slices.Length && rows.Length != 0; column++)
        {
            var (from, to) = bounds[column];
            for (int i = from; i < to; i++)
            {
                CheckCancellation(i, token);
                Sample sample = samples[i];
                if (Matches(sample, threadId, scope) && binsByMethod.TryGetValue(_stacks[sample.Stack].Method, out TraceCost[]? bins))
                {
                    bins[column] += new TraceCost(1, weights?[i].EstimatedBytes ?? 0);
                }
            }
        }

        return new HeatmapData(metric, mode, slices, rows);
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

    /// <summary>Aggregates the selected sample source into call paths, methods and allocation types.</summary>
    public ProfileAnalysis Analyze(TraceMetric metric, int? threadId, TraceScope scope, TraceRange? range, CancellationToken token)
    {
        Sample[] samples = GetSamples(metric);
        int from = range.HasValue ? FindSampleAtOrAfter(samples, range.Value.StartMs) : 0;
        int to = range.HasValue ? FindSampleAtOrAfter(samples, range.Value.EndMs) : samples.Length;
        if (from == to)
        {
            return new ProfileAnalysis(null, [], [], default);
        }

        AllocationWeight[]? weights = GetAllocationWeights(metric);
        TraceCost[] typeCosts = weights == null ? [] : new TraceCost[_allocations!.Types.Length];
        var selfCosts = new TraceCost[_stacks.Length];
        TraceCost selectedCost = default;
        for (int i = from; i < to; i++)
        {
            CheckCancellation(i, token);
            Sample sample = samples[i];
            if (!Matches(sample, threadId, scope))
            {
                continue;
            }

            var cost = new TraceCost(1, weights?[i].EstimatedBytes ?? 0);
            if (weights != null)
            {
                typeCosts[weights[i].Type] += cost;
            }

            selfCosts[sample.Stack] += cost;
            selectedCost += cost;
        }

        if (selectedCost.Samples == 0)
        {
            return new ProfileAnalysis(null, [], [], default);
        }

        var roots = new List<CallNode>();
        var children = new List<CallNode>?[_stacks.Length];
        var totalCosts = (TraceCost[])selfCosts.Clone();
        var methodTotals = new TraceCost[_methods.Length];
        var methodSelf = new TraceCost[_methods.Length];
        for (int i = _stacks.Length - 1; i >= 0; i--)
        {
            CheckCancellation(i, token);
            TraceCost total = totalCosts[i];
            if (total.Samples == 0)
            {
                continue;
            }

            StackInfo stack = _stacks[i];
            TraceCost self = selfCosts[i];
            methodSelf[stack.Method] += self;
            if (!stack.RepeatedMethod)
            {
                methodTotals[stack.Method] += total;
            }

            List<CallNode>? branch = children[i];
            branch?.Sort(CompareByWeight);
            var node = new CallNode(_methods[stack.Method], total, self, branch is null ? [] : branch);
            if (stack.Parent < 0)
            {
                roots.Add(node);
                continue;
            }

            totalCosts[stack.Parent] += total;
            (children[stack.Parent] ??= []).Add(node);
        }

        roots.Sort(CompareByWeight);
        double? interval = metric == TraceMetric.Time ? SampleIntervalMs : null;
        var methods = new List<MethodSummary>();
        for (int i = 0; i < _methods.Length; i++)
        {
            if (methodTotals[i].Samples > 0)
            {
                methods.Add(new MethodSummary(_methods[i], methodTotals[i], methodSelf[i], interval));
            }
        }

        var types = new List<AllocationTypeSummary>();
        for (int i = 0; i < typeCosts.Length; i++)
        {
            if (typeCosts[i].Samples > 0)
            {
                types.Add(new AllocationTypeSummary(_allocations!.Types[i], typeCosts[i]));
            }
        }
        types.Sort(static (x, y) =>
        {
            int result = y.Cost.EstimatedBytes.CompareTo(x.Cost.EstimatedBytes);
            return result != 0 ? result : StringComparer.Ordinal.Compare(x.TypeName, y.TypeName);
        });

        string rootLabel = metric == TraceMetric.Allocation ? "Selected allocations" : "Selected samples";
        return new ProfileAnalysis(new CallNode(rootLabel, selectedCost, default, roots), methods.ToArray(), types.ToArray(), selectedCost);
    }

    private Sample[] GetSamples(TraceMetric metric) => metric == TraceMetric.Allocation ? _allocations?.Samples ?? [] : _samples;

    private AllocationWeight[]? GetAllocationWeights(TraceMetric metric) => metric == TraceMetric.Allocation ? _allocations?.Weights : null;

    private static int FindSampleAtOrAfter(Sample[] samples, double timeMs)
    {
        int lo = 0;
        int hi = samples.Length;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (samples[mid].TimeMs < timeMs)
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

    private static int CompareByWeight(CallNode x, CallNode y)
    {
        int result = y.Total.Weight.CompareTo(x.Total.Weight);
        return result != 0 ? result : StringComparer.Ordinal.Compare(x.Method, y.Method);
    }
}
