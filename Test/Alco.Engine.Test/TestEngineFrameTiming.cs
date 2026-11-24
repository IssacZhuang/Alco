using System.Diagnostics;
using Alco.Graphics;
using Alco.Graphics.LowLevel;
using NUnit.Framework;

namespace Alco.Engine.Test;

/// <summary>
/// Verifies frame notification order, wall-clock metrics, and main-loop frame pacing.
/// </summary>
public class TestEngineFrameTiming
{
    /// <summary>
    /// Verifies that submitting enqueued contexts via <see cref="ViewPresenter.EndFrame"/> allows deferred <see cref="RenderGraph.Resize"/> handlers to run without submission callbacks.
    /// </summary>
    [Test]
    public void PresenterSubmissionMarksAttachedRenderGraphBeforeResize()
    {
        GPUDevice device = Gpu.CreateDevice(new DeviceDesc { Backend = GraphicsBackend.NoGPU, Headless = true });
        RenderingSystem rendering = new(device, textureHeapByteCount: 1024 * 1024, bufferHeapByteCount: 1024 * 1024);
        RenderContext context = new(rendering);
        RenderGraph graph = new(rendering, new Extent2D(64, 64));
        try
        {
            using NoView view = new(new ViewSetting(64, 64, "Render graph resize"));
            using ViewPresenter presenter = new(view, rendering);
            presenter.OnResize += size => graph.Resize(new Extent2D(size.X, size.Y));

            presenter.BeginFrame();
            context.BeginFrame();
            graph.Execute(context);
            presenter.EnqueueFrameContext(context);
            view.DoResize(new uint2(96, 80));
            presenter.EndFrame();

            Assert.That(graph.Viewport, Is.EqualTo(new Extent2D(96, 80)));
        }
        finally
        {
            graph.Dispose();
            context.Dispose();
            rendering.Dispose();
            device.Destroy();
        }
    }

    /// <summary>
    /// Verifies that an exception in <see cref="ViewPresenter.OnBeforePresent"/> discards enqueued contexts cleanly so associated render graphs can resize and continue.
    /// </summary>
    [Test]
    public void PresenterEndFrameDiscardsContextsAndAttachedGraphsOnBeforePresentFailure()
    {
        GPUDevice device = Gpu.CreateDevice(new DeviceDesc { Backend = GraphicsBackend.NoGPU, Headless = true });
        RenderingSystem rendering = new(device, textureHeapByteCount: 1024 * 1024, bufferHeapByteCount: 1024 * 1024);
        RenderContext context = new(rendering);
        RenderGraph graph = new(rendering, new Extent2D(64, 64));
        try
        {
            using NoView view = new(new ViewSetting(64, 64, "Discard on failure"));
            using ViewPresenter presenter = new(view, rendering);
            presenter.OnBeforePresent += () => throw new InvalidOperationException("Simulated pre-present failure");

            presenter.BeginFrame();
            context.BeginFrame();
            graph.Execute(context);
            presenter.EnqueueFrameContext(context);

            Assert.Throws<InvalidOperationException>(() => presenter.EndFrame());

            graph.Resize(new Extent2D(96, 80));
            Assert.That(graph.Viewport, Is.EqualTo(new Extent2D(96, 80)));

            context.BeginFrame();
            context.Submit();
            rendering.WaitIdle();
        }
        finally
        {
            graph.Dispose();
            context.Dispose();
            rendering.Dispose();
            device.Destroy();
        }
    }

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
