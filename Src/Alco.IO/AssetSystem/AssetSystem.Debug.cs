using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Alco.IO;

public sealed partial class AssetSystem
{
    /// <summary>
    /// A stack-allocated profiler scope that measures asset load time.
    /// Uses <see cref="FixedString128"/> for zero-heap label building and
    /// <see cref="Stopwatch.GetTimestamp"/> for timing.
    /// </summary>
    private struct AssetProfilerScope : IDisposable
    {
        private static readonly double MillisecondMultiplier = 1000.0 / Stopwatch.Frequency;

        private FixedString128 _label;
        private long _startTime;
        private bool _isFailed;

        /// <summary>
        /// Initializes a new profiling scope for an asset load operation.
        /// </summary>
        /// <param name="assetName">The filename of the asset being loaded.</param>
        /// <param name="typeName">The type name of the asset being loaded.</param>
        public AssetProfilerScope(string assetName, string typeName)
        {
            _label = new FixedString128();
            _label.Append("Load ");
            _label.Append(assetName);
            _label.Append("(");
            _label.Append(typeName);
            _label.Append(")");
            _startTime = Stopwatch.GetTimestamp();
            _isFailed = false;
        }

        /// <summary>
        /// Marks this profiling scope as failed, suppressing the success log.
        /// </summary>
        public void Fail() => _isFailed = true;

        /// <summary>
        /// Ends the profiling scope and logs the elapsed time.
        /// </summary>
        public void Dispose()
        {
            if (_isFailed) return;
            long elapsed = Stopwatch.GetTimestamp() - _startTime;
            double ms = Math.Round(elapsed * MillisecondMultiplier, 3);
            _label.Append(' ');
            _label.Append(ms);
            _label.Append("ms");
            Log.Success(_label.AsReadOnlySpan());
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private AssetProfilerScope? StartProfile(string name, Type type)
    {
        if (!IsProfileEnabled)
        {
            return null;
        }

        return new AssetProfilerScope(name, type.Name);
    }

    // for unit test only
    internal bool DebugIsAssetCached(string assetPath)
    {
        return _assetLookup.TryGetValue(assetPath, out AssetHandle? handle) && handle.CachedAsset != null;
    }
}
