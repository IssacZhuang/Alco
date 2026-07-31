using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Alco.Engine;

/// <summary>
/// Tracks wall-clock frame cadence without allocating on the frame hot path.
/// </summary>
internal struct EngineProfiler
{
    private const double UpdateIntervalSeconds = 0.5;
    private const int FrameTimeSampleCapacity = 256;

    private readonly double[] _frameTimeSamples;
    private readonly double[] _sortedFrameTimeSamples;

    private long _lastFrameTimestamp;
    private double _windowElapsedSeconds;
    private int _windowFrameCount;
    private int _nextSampleIndex;
    private int _sampleCount;
    private int _fps;
    private int _onePercentLowFps;
    private float _frameTime;
    private float _p99FrameTime;

    /// <summary>
    /// Gets the average main-loop frame rate measured over the latest reporting window.
    /// </summary>
    public readonly int FPS
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _fps;
    }

    /// <summary>
    /// Gets the one-percent-low frame rate calculated from recent wall-clock frame times.
    /// </summary>
    public readonly int OnePercentLowFPS
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _onePercentLowFps;
    }

    /// <summary>
    /// Gets the average wall-clock duration between main-loop frames, in seconds.
    /// </summary>
    public readonly float FrameTime
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _frameTime;
    }

    /// <summary>
    /// Gets the 99th-percentile wall-clock frame time from recent samples, in seconds.
    /// </summary>
    public readonly float P99FrameTime
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _p99FrameTime;
    }

    /// <summary>
    /// Initializes a new frame cadence profiler.
    /// </summary>
    public EngineProfiler()
    {
        _frameTimeSamples = new double[FrameTimeSampleCapacity];
        _sortedFrameTimeSamples = new double[FrameTimeSampleCapacity];
    }

    /// <summary>
    /// Records the start of a main-loop frame using a monotonic timestamp.
    /// </summary>
    /// <param name="timestamp">Timestamp expressed in <see cref="Stopwatch.Frequency"/> units.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Update(long timestamp)
    {
        if (_lastFrameTimestamp == 0)
        {
            _lastFrameTimestamp = timestamp;
            return;
        }

        long elapsedTicks = timestamp - _lastFrameTimestamp;
        _lastFrameTimestamp = timestamp;
        if (elapsedTicks <= 0)
        {
            return;
        }

        double elapsedSeconds = (double)elapsedTicks / Stopwatch.Frequency;
        AddFrameTimeSample(elapsedSeconds);

        _windowElapsedSeconds += elapsedSeconds;
        _windowFrameCount++;
        if (_windowElapsedSeconds < UpdateIntervalSeconds)
        {
            return;
        }

        RefreshMetrics();
        _windowElapsedSeconds = 0;
        _windowFrameCount = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AddFrameTimeSample(double elapsedSeconds)
    {
        _frameTimeSamples[_nextSampleIndex] = elapsedSeconds;
        _nextSampleIndex = (_nextSampleIndex + 1) % FrameTimeSampleCapacity;
        _sampleCount = Math.Min(_sampleCount + 1, FrameTimeSampleCapacity);
    }

    private void RefreshMetrics()
    {
        if (_windowFrameCount <= 0 || _windowElapsedSeconds <= 0 || _sampleCount <= 0)
        {
            return;
        }

        _fps = (int)Math.Round(_windowFrameCount / _windowElapsedSeconds);
        _frameTime = (float)(_windowElapsedSeconds / _windowFrameCount);

        Array.Copy(_frameTimeSamples, _sortedFrameTimeSamples, _sampleCount);
        Array.Sort(_sortedFrameTimeSamples, 0, _sampleCount);

        int p99Index = Math.Clamp((int)Math.Ceiling(_sampleCount * 0.99) - 1, 0, _sampleCount - 1);
        _p99FrameTime = (float)_sortedFrameTimeSamples[p99Index];

        int slowFrameCount = Math.Max(1, (int)Math.Ceiling(_sampleCount * 0.01));
        int slowFrameStart = _sampleCount - slowFrameCount;
        double slowFrameTimeTotal = 0;
        for (int i = slowFrameStart; i < _sampleCount; i++)
        {
            slowFrameTimeTotal += _sortedFrameTimeSamples[i];
        }

        double onePercentLowFrameTime = slowFrameTimeTotal / slowFrameCount;
        _onePercentLowFps = onePercentLowFrameTime > 0
            ? (int)Math.Round(1.0 / onePercentLowFrameTime)
            : 0;
    }
}
