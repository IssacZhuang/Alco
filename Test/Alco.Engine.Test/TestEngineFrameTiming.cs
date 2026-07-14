using System.Diagnostics;
using NUnit.Framework;

namespace Alco.Engine.Test;

/// <summary>
/// Verifies wall-clock frame metrics and main-loop frame pacing.
/// </summary>
public class TestEngineFrameTiming
{
    /// <summary>
    /// Verifies that stable frame intervals produce matching average and low frame rates.
    /// </summary>
    [Test]
    public void ProfilerReportsStableWallClockCadence()
    {
        var profiler = new EngineProfiler();
        long timestamp = 1;
        long frameInterval = Stopwatch.Frequency / 120;
        profiler.Update(timestamp);

        for (int i = 0; i < 100; i++)
        {
            timestamp += frameInterval;
            profiler.Update(timestamp);
        }

        Assert.That(profiler.FPS, Is.EqualTo(120));
        Assert.That(profiler.OnePercentLowFPS, Is.EqualTo(120));
        Assert.That(profiler.FrameTime, Is.EqualTo(1f / 120f).Within(0.00001f));
        Assert.That(profiler.P99FrameTime, Is.EqualTo(1f / 120f).Within(0.00001f));
    }

    /// <summary>
    /// Verifies that rare long frames remain visible in one-percent-low and percentile metrics.
    /// </summary>
    [Test]
    public void ProfilerExposesRareLongFrames()
    {
        var profiler = new EngineProfiler();
        long timestamp = 1;
        long regularFrameInterval = Stopwatch.Frequency / 200;
        long longFrameInterval = Stopwatch.Frequency * 40 / 1000;
        profiler.Update(timestamp);

        for (int i = 0; i < 200; i++)
        {
            timestamp += i < 2 ? longFrameInterval : regularFrameInterval;
            profiler.Update(timestamp);
        }

        Assert.That(profiler.FPS, Is.EqualTo(200));
        Assert.That(profiler.OnePercentLowFPS, Is.EqualTo(25));
        Assert.That(profiler.P99FrameTime, Is.EqualTo(0.04f).Within(0.00001f));
    }

    /// <summary>
    /// Verifies that the frame pacer does not complete target frame boundaries early.
    /// </summary>
    [Test]
    public void FramePacerHonorsTargetCadence()
    {
        var timer = new EngineTimer();
        timer.Start(100);
        var stopwatch = Stopwatch.StartNew();

        for (int i = 0; i < 10; i++)
        {
            timer.WaitForNextFrame();
        }

        Assert.That(stopwatch.Elapsed.TotalMilliseconds, Is.GreaterThanOrEqualTo(90));
    }

    /// <summary>
    /// Verifies that a missed deadline is re-phased instead of producing immediate catch-up frames.
    /// </summary>
    [Test]
    public void FramePacerDoesNotBurstAfterOverrun()
    {
        var timer = new EngineTimer();
        timer.Start(100);

        for (int i = 0; i < 3; i++)
        {
            timer.WaitForNextFrame();
        }

        Thread.Sleep(25);
        timer.WaitForNextFrame();

        var stopwatch = Stopwatch.StartNew();
        timer.WaitForNextFrame();

        Assert.That(stopwatch.Elapsed.TotalMilliseconds, Is.GreaterThanOrEqualTo(5));
    }
}
