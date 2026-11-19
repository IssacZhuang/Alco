using System.Diagnostics.CodeAnalysis;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.EventPipe;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;

namespace Alco.Profiler;

internal sealed partial class TraceProfile
{
    /// <summary>Identifies the EventPipe provider for Alco loop boundaries.</summary>
    public const string LoopTimingProviderName = "Alco-Engine-Timing";

    /// <summary>Reads managed stack samples, allocation samples and loop boundaries from a .nettrace file.</summary>
    public static bool TryOpen(string filePath, [NotNullWhen(true)] out TraceProfile? profile, out string error)
    {
        string scratch = Path.Combine(Path.GetTempPath(), $"alco-profiler-{Guid.NewGuid():N}.etlx");
        try
        {
            var options = new TraceLogOptions { LocalSymbolsOnly = true };
            string indexPath = TraceLog.CreateFromEventPipeDataFile(filePath, scratch, options);
            using var log = new TraceLog(indexPath);
            using TraceLogEventSource source = log.Events.GetSource();
            var reader = new SampleReader();
            var parser = new SampleProfilerTraceEventParser(source);
            parser.ThreadSample += reader.ReadTimeSample;
            source.Clr.AllocationSampling += reader.ReadAllocationSample;
            source.Dynamic.AddCallbackForProviderEvents(static (provider, _) => provider == LoopTimingProviderName
                ? EventFilterResponse.AcceptEvent : EventFilterResponse.RejectProvider, reader.ReadTimingMarker);
            source.Process();
            return reader.TryBuild(log.EventsLost, out profile, out error);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException
            or ArgumentException or FormatException or InvalidOperationException or ApplicationException)
        {
            profile = null;
            error = exception.Message;
            return false;
        }
        finally
        {
            try
            {
                File.Delete(scratch);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private sealed class SampleReader
    {
        private const int MaxIntervalSamples = 4096;
        // The CoreCLR AllocationSampled contract fixes this probability per allocated byte.
        private const double AllocationSampleByteProbability = 1d / 102_400;

        private readonly Dictionary<CallStackIndex, int> _sourceStacks = new();
        private readonly Dictionary<(int Parent, int Method), int> _paths = new();
        private readonly Dictionary<string, int> _methodIds = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _typeIds = new(StringComparer.Ordinal);
        private readonly Dictionary<int, (ThreadChoice Thread, int TimeCount, int AllocationCount, int LoopCount)> _threads = new();
        private readonly List<TraceCallStack> _unseen = new();
        private readonly List<StackInfo> _stacks = new();
        private readonly List<string> _methods = new();
        private readonly List<Sample> _samples = new();
        private readonly List<(Sample Sample, AllocationWeight Weight)> _allocations = new();
        private readonly List<string> _types = new();
        private readonly Dictionary<(bool Tick, long Sequence, int ThreadId), double> _pendingInvocations = new();
        private readonly List<TraceSlice> _ticks = new();
        private readonly List<TraceSlice> _updates = new();

        /// <summary>Retains a thread-time stack sample.</summary>
        public void ReadTimeSample(ClrThreadSampleTraceData sample) => _samples.Add(ReadSample(sample, isAllocation: false));

        /// <summary>Retains a randomized allocation sample and its estimated byte contribution.</summary>
        public void ReadAllocationSample(AllocationSampledTraceData sample)
        {
            double size = sample.ObjectSize;
            if (size <= 0)
            {
                return;
            }

            string typeName = sample.TypeName;
            if (string.IsNullOrEmpty(typeName))
            {
                typeName = "[Unknown type]";
            }
            if (!_typeIds.TryGetValue(typeName, out int type))
            {
                type = _types.Count;
                _types.Add(typeName);
                _typeIds.Add(typeName, type);
            }

            double probability = 1 - Math.Pow(1 - AllocationSampleByteProbability, size);
            _allocations.Add((ReadSample(sample, isAllocation: true), new AllocationWeight(type, size / probability)));
        }

        private Sample ReadSample(TraceEvent sample, bool isAllocation)
        {
            TraceThread? thread = sample.Thread();
            int tid = sample.ThreadID;
            int key = thread != null ? (int)thread.ThreadIndex : tid >= 0 ? ~tid : int.MinValue;
            if (!_threads.TryGetValue(key, out var info))
            {
                string label = string.IsNullOrEmpty(thread?.ThreadInfo) ? $"Thread {tid}" : $"{thread.ThreadInfo} ({tid})";
                info = (new ThreadChoice(key, label), 0, 0, 0);
            }

            int stack = sample.CallStack() is TraceCallStack callStack
                ? GetStack(callStack)
                : InternPath(-1, "[No stack captured]");
            int loop = (_stacks[stack].Scope & TraceScope.GameLoop) != 0 ? 1 : 0;
            _threads[key] = (info.Thread, info.TimeCount + (isAllocation ? 0 : 1),
                info.AllocationCount + (isAllocation ? 1 : 0), info.LoopCount + loop);
            return new Sample(sample.TimeStampRelativeMSec, key, stack);
        }

        /// <summary>Pairs only complete boundaries from the same invocation and thread.</summary>
        public void ReadTimingMarker(TraceEvent marker)
        {
            int id = (int)marker.ID;
            if (id is < 1 or > 4 || marker.PayloadNames.Length != 1 || marker.PayloadValue(0) is not long sequence || sequence <= 0)
            {
                return;
            }

            bool tick = id is 1 or 2;
            var key = (tick, sequence, marker.ThreadID);
            double time = marker.TimeStampRelativeMSec;
            if (id is 1 or 3)
            {
                _pendingInvocations[key] = time;
                return;
            }

            if (_pendingInvocations.Remove(key, out double start) && time >= start)
            {
                (tick ? _ticks : _updates).Add(new TraceSlice(new TraceRange(start, time), sequence));
            }
        }

        /// <summary>Creates a model from stack samples, allocations or closed loop intervals.</summary>
        public bool TryBuild(int lostEvents, [NotNullWhen(true)] out TraceProfile? profile, out string error)
        {
            if (_samples.Count == 0 && _allocations.Count == 0 && _ticks.Count == 0 && _updates.Count == 0)
            {
                profile = null;
                error = "No .NET thread samples, allocation samples or complete loop timings found.";
                return false;
            }

            _samples.Sort(static (x, y) => x.TimeMs.CompareTo(y.TimeMs));
            _ticks.Sort(CompareSlices);
            _updates.Sort(CompareSlices);
            ThreadChoice[] threads = _threads.Values
                .OrderByDescending(thread => thread.LoopCount)
                .ThenByDescending(thread => thread.TimeCount)
                .ThenByDescending(thread => thread.AllocationCount)
                .ThenBy(thread => thread.Thread.Label, StringComparer.Ordinal)
                .Select(thread => thread.LoopCount > 0
                    ? thread.Thread with { Label = $"Game loop · {thread.Thread.Label}", IsGameLoop = true }
                    : thread.Thread)
                .ToArray();
            double? intervalMs = null;
            if (_samples.Count > 0)
            {
                var busiest = _threads.Values.MaxBy(thread => thread.TimeCount);
                intervalMs = EstimateInterval(busiest.Thread.Id!.Value, busiest.TimeCount);
            }

            profile = new TraceProfile(_methods.ToArray(), _stacks.ToArray(), _samples.ToArray(), GetAllocationData(), threads,
                _ticks.ToArray(), _updates.ToArray(), intervalMs, lostEvents);
            error = string.Empty;
            return true;
        }

        private AllocationData? GetAllocationData()
        {
            if (_allocations.Count == 0)
            {
                return null;
            }

            _allocations.Sort(static (x, y) => x.Sample.TimeMs.CompareTo(y.Sample.TimeMs));
            var samples = new Sample[_allocations.Count];
            var weights = new AllocationWeight[_allocations.Count];
            for (int i = 0; i < samples.Length; i++)
            {
                (samples[i], weights[i]) = _allocations[i];
            }
            return new AllocationData(samples, weights, _types.ToArray());
        }

        private static int CompareSlices(TraceSlice x, TraceSlice y)
        {
            int comparison = x.Range.StartMs.CompareTo(y.Range.StartMs);
            return comparison != 0 ? comparison : x.Sequence.CompareTo(y.Sequence);
        }

        private int GetStack(TraceCallStack source)
        {
            _unseen.Clear();
            int parent = -1;
            for (TraceCallStack? frame = source; frame != null; frame = frame.Caller)
            {
                if (_sourceStacks.TryGetValue(frame.CallStackIndex, out int stack))
                {
                    parent = stack;
                    break;
                }

                _unseen.Add(frame);
            }

            for (int i = _unseen.Count - 1; i >= 0; i--)
            {
                TraceCallStack frame = _unseen[i];
                TraceCodeAddress address = frame.CodeAddress;
                string name = address.FullMethodName;
                string module = address.ModuleName;
                string label = string.IsNullOrEmpty(name)
                    ? $"[Unresolved: {module} 0x{address.Address:X}]"
                    : string.IsNullOrEmpty(module) ? name : $"{name}  [{module}]";
                parent = InternPath(parent, label);
                _sourceStacks.Add(frame.CallStackIndex, parent);
            }

            return parent;
        }

        private int InternPath(int parent, string label)
        {
            if (!_methodIds.TryGetValue(label, out int method))
            {
                method = _methods.Count;
                _methods.Add(label);
                _methodIds.Add(label, method);
            }

            if (_paths.TryGetValue((parent, method), out int path))
            {
                return path;
            }

            bool repeated = false;
            for (int ancestor = parent; ancestor >= 0; ancestor = _stacks[ancestor].Parent)
            {
                if (_stacks[ancestor].Method == method)
                {
                    repeated = true;
                    break;
                }
            }

            TraceScope scope = GetScope(label);
            if (parent >= 0)
            {
                scope |= _stacks[parent].Scope;
            }

            path = _stacks.Count;
            _stacks.Add(new StackInfo(method, parent, scope, repeated));
            _paths.Add((parent, method), path);
            return path;
        }

        private double? EstimateInterval(int threadId, int count)
        {
            int stride = Math.Max(1, (int)Math.Ceiling(count / (double)MaxIntervalSamples));
            var intervals = new List<double>(Math.Min(MaxIntervalSamples, count));
            double previous = double.NaN;
            int index = 0;
            foreach (Sample sample in _samples)
            {
                if (sample.ThreadId != threadId)
                {
                    continue;
                }

                double elapsed = sample.TimeMs - previous;
                if (index++ % stride == 0 && elapsed > 0)
                {
                    intervals.Add(elapsed);
                }

                previous = sample.TimeMs;
            }

            if (intervals.Count == 0)
            {
                return null;
            }

            intervals.Sort();
            return intervals[intervals.Count / 2];
        }

        private static TraceScope GetScope(string label)
        {
            const string prefix = "Alco.Engine.GameEngine.";
            if (!label.StartsWith(prefix, StringComparison.Ordinal))
            {
                return TraceScope.All;
            }

            ReadOnlySpan<char> method = label.AsSpan(prefix.Length);
            int suffix = method.IndexOfAny('(', ' ');
            if (suffix >= 0)
            {
                method = method[..suffix];
            }

            return method switch
            {
                "Run" or "InternalRun" => TraceScope.GameLoop,
                "ExecuteTickBody" or "InternalTick" => TraceScope.Tick | TraceScope.GameLoop,
                "InternalUpdate" => TraceScope.Update | TraceScope.GameLoop,
                _ => TraceScope.All,
            };
        }
    }
}
