using System.Diagnostics.CodeAnalysis;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.EventPipe;

namespace Alco.Profiler;

internal sealed partial class TraceProfile
{
    /// <summary>Reads timestamped managed thread samples and resolves their methods from a .nettrace file.</summary>
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
            parser.ThreadSample += reader.Read;
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

        private readonly Dictionary<CallStackIndex, int> _sourceStacks = new();
        private readonly Dictionary<(int Parent, int Method), int> _paths = new();
        private readonly Dictionary<string, int> _methodIds = new(StringComparer.Ordinal);
        private readonly Dictionary<int, (ThreadChoice Thread, int Count, int LoopCount)> _threads = new();
        private readonly List<TraceCallStack> _unseen = new();
        private readonly List<StackInfo> _stacks = new();
        private readonly List<string> _methods = new();
        private readonly List<Sample> _samples = new();

        /// <summary>Retains a sample and interns the managed stack it observed.</summary>
        public void Read(ClrThreadSampleTraceData sample)
        {
            TraceThread? thread = sample.Thread();
            int tid = sample.ThreadID;
            int key = thread != null ? (int)thread.ThreadIndex : tid >= 0 ? ~tid : int.MinValue;
            if (!_threads.TryGetValue(key, out var info))
            {
                string label = string.IsNullOrEmpty(thread?.ThreadInfo) ? $"Thread {tid}" : $"{thread.ThreadInfo} ({tid})";
                info = (new ThreadChoice(key, label), 0, 0);
            }

            int stack = sample.CallStack() is TraceCallStack callStack
                ? GetStack(callStack)
                : InternPath(-1, "[No stack captured]");
            int loopSamples = (_stacks[stack].Scope & TraceScope.GameLoop) != 0 ? 1 : 0;
            _threads[key] = (info.Thread, info.Count + 1, info.LoopCount + loopSamples);
            _samples.Add(new Sample(sample.TimeStampRelativeMSec, key, stack));
        }

        /// <summary>Creates a trace model when the file contains stack sampling events.</summary>
        public bool TryBuild(int lostEvents, [NotNullWhen(true)] out TraceProfile? profile, out string error)
        {
            if (_samples.Count == 0)
            {
                profile = null;
                error = "No .NET thread samples found. Collect with the dotnet-sampled-thread-time profile.";
                return false;
            }

            _samples.Sort(static (x, y) => x.TimeMs.CompareTo(y.TimeMs));
            ThreadChoice[] threads = _threads.Values
                .OrderByDescending(thread => thread.LoopCount)
                .ThenByDescending(thread => thread.Count)
                .ThenBy(thread => thread.Thread.Label, StringComparer.Ordinal)
                .Select(thread => thread.LoopCount > 0
                    ? thread.Thread with { Label = $"Game loop · {thread.Thread.Label}", IsGameLoop = true }
                    : thread.Thread)
                .ToArray();
            var busiest = _threads.Values.MaxBy(thread => thread.Count);
            double? intervalMs = EstimateInterval(busiest.Thread.Id!.Value, busiest.Count);
            profile = new TraceProfile(_methods.ToArray(), _stacks.ToArray(), _samples.ToArray(), threads, intervalMs, lostEvents);
            error = string.Empty;
            return true;
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
