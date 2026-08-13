using NUnit.Framework;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.Rendering.Test;

/// <summary>
/// Tests of the RAII render-pass scopes (<see cref="RenderPassScope"/>) handed out by
/// <see cref="RenderContext.BeginPass(GPUFrameBuffer, ReadOnlySpan{ClearColorData}, float?, uint?, ReadOnlySpan{AttachmentOps}, AttachmentOps?)"/>
/// and <see cref="SubRenderContext.BeginPass"/>: standalone auto-submission, misuse
/// guards, listener notifications, bundle recording, and the render graph's
/// one-submission-per-frame model. Driven by the NoGPU backend.
/// </summary>
[TestFixture]
public sealed class TestRenderPassScope
{
    private sealed class FakeListener : ICommandListener
    {
        public int BeginCount;
        public int EndCount;

        public void OnCommandBegin() => BeginCount++;
        public void OnCommandEnd() => EndCount++;
    }

    /// <summary>A graph node that opens one pass on the frame-shared context.</summary>
    private sealed class PassNode : IRenderGraphNode
    {
        private readonly RenderGraphTexture _target;
        public bool IsEnabled { get; set; } = true;

        public PassNode(RenderGraphTexture target, bool declaresOutput)
        {
            _target = target;
            DeclaresOutput = declaresOutput;
        }

        public bool DeclaresOutput { get; }

        public void Setup(RenderGraphBuilder builder)
        {
            builder.Write(_target);
            if (DeclaresOutput)
            {
                builder.ProducesOutput();
            }
        }

        public void Execute(in RenderGraphContext context)
        {
            using (context.RenderContext.BeginPass(_target.Texture.FrameBuffer))
            {
            }
        }
    }

    /// <summary>A graph node that declares a write but records no GPU work.</summary>
    private sealed class NoopNode : IRenderGraphNode
    {
        private readonly RenderGraphTexture _target;
        public bool IsEnabled { get; set; } = true;

        public NoopNode(RenderGraphTexture target)
        {
            _target = target;
        }

        public void Setup(RenderGraphBuilder builder)
        {
            builder.Write(_target);
            builder.ProducesOutput();
        }

        public void Execute(in RenderGraphContext context)
        {
        }
    }

    private DummyRenderingSystemHost _host;
    private RenderingSystem _rendering;
    private GPUDevice _device;
    private GPUAttachmentLayout _layout;
    private RenderTexture _target;

    [SetUp]
    public void SetUp()
    {
        _host = Utility.CreateRenderingSystem();
        _rendering = _host.RenderingSystem;
        _device = _rendering.GraphicsDevice;
        _layout = _device.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            [new ColorAttachment(PixelFormat.RGBA8Unorm)], null, "scope_color"));
        _target = _rendering.CreateRenderTexture(_layout, 32, 32, "scope_target");
    }

    [TearDown]
    public void TearDown()
    {
        _target.Dispose();
        _layout.Dispose();
        _host.Dispose();
    }

    [Test(Description = "A standalone context submits its command buffer when the pass scope is disposed")]
    public void StandaloneContextSubmitsOnScopeDispose()
    {
        using RenderContext context = _rendering.CreateRenderContext("test");
        int before = _rendering.ScheduledSubmissionCount;

        using (context.BeginPass(_target.FrameBuffer))
        {
        }

        Assert.That(_rendering.ScheduledSubmissionCount, Is.EqualTo(before + 1));
    }

    [Test(Description = "Two sequential passes on one standalone context submit once each (the old Begin/End behavior)")]
    public void SequentialPassesSubmitOnceEach()
    {
        using RenderContext context = _rendering.CreateRenderContext("test");
        int before = _rendering.ScheduledSubmissionCount;

        using (context.BeginPass(_target.FrameBuffer))
        {
        }
        using (context.BeginPass(_target.FrameBuffer))
        {
        }

        Assert.That(_rendering.ScheduledSubmissionCount, Is.EqualTo(before + 2));
    }

    [Test(Description = "Calls on a disposed scope throw InvalidOperationException")]
    public void CallsOnClosedScopeThrow()
    {
        using RenderContext context = _rendering.CreateRenderContext("test");
        RenderPassScope pass = context.BeginPass(_target.FrameBuffer);
        pass.Dispose();

        Assert.Throws<InvalidOperationException>(() => pass.SetStencilReference(1));
        Assert.Throws<InvalidOperationException>(() => pass.Dispose());
    }

    [Test(Description = "Beginning a second pass while one is open throws InvalidOperationException")]
    public void NestedBeginPassThrows()
    {
        using RenderContext context = _rendering.CreateRenderContext("test");
        using (context.BeginPass(_target.FrameBuffer))
        {
            Assert.Throws<InvalidOperationException>(() => context.BeginPass(_target.FrameBuffer));
        }
    }

    [Test(Description = "Listeners are notified exactly once per pass, on the recycled scope identity")]
    public void ListenersFireOncePerPass()
    {
        using RenderContext context = _rendering.CreateRenderContext("test");
        var listener = new FakeListener();
        context.Pass.AddListener(listener);

        using (context.BeginPass(_target.FrameBuffer))
        {
        }
        using (context.BeginPass(_target.FrameBuffer))
        {
        }

        Assert.That(listener.BeginCount, Is.EqualTo(2));
        Assert.That(listener.EndCount, Is.EqualTo(2));
    }

    [Test(Description = "A sub context records a bundle through its scope; the bundle replays into a pass")]
    public void SubRenderContextRecordsAndReplaysBundle()
    {
        using SubRenderContext sub = _rendering.CreateSubRenderContext("test_sub");

        using (sub.BeginPass(_layout))
        {
        }

        // HasBuffer is backend-defined before the first recording (NoGPU reports true
        // always); after a recording it must report a recorded bundle.
        Assert.That(sub.HasBuffer, Is.True);

        using RenderContext context = _rendering.CreateRenderContext("test");
        using (RenderPassScope pass = context.BeginPass(_target.FrameBuffer))
        {
            Assert.DoesNotThrow(() => pass.ExecuteSubContext(sub));
        }
    }

    [Test(Description = "Pass-only operations throw while recording a render bundle")]
    public void BundleScopeRejectsPassOnlyOperations()
    {
        using SubRenderContext sub = _rendering.CreateSubRenderContext("test_sub");
        using SubRenderContext other = _rendering.CreateSubRenderContext("test_other");
        using (RenderPassScope pass = sub.BeginPass(_layout))
        {
            Assert.Throws<InvalidOperationException>(() => pass.SetScissorRect(0, 0, 1, 1));
            Assert.Throws<InvalidOperationException>(() => pass.SetStencilReference(1));
            Assert.Throws<InvalidOperationException>(() => pass.ExecuteSubContext(other));
        }
    }

    [Test(Description = "Listeners on a graph-shared context fire once per frame (buffer cycle), no matter how many passes the frame contains")]
    public void ListenersFireOncePerFrameOnSharedGraphContext()
    {
        using var graph = new RenderGraph(_rendering, 32, 32, "test_graph");
        RenderGraphTexture a = graph.CreateTransient(new RenderGraphTextureDescriptor(_layout, name: "a"));
        RenderGraphTexture b = graph.CreateTransient(new RenderGraphTextureDescriptor(_layout, name: "b"));
        graph.Use(new PassNode(a, declaresOutput: false));
        graph.Use(new PassNode(b, declaresOutput: true));

        var listener = new FakeListener();
        graph.RenderContext.Pass.AddListener(listener);

        graph.Execute(null);

        Assert.That(listener.BeginCount, Is.EqualTo(1),
            "Two passes on the shared context must still be a single listener begin for the frame.");
        Assert.That(listener.EndCount, Is.EqualTo(1));
    }

    [Test(Description = "Listeners on a sub context fire once per bundle recording")]
    public void SubRenderContextListenersFirePerRecording()
    {
        using SubRenderContext sub = _rendering.CreateSubRenderContext("test_sub");
        var listener = new FakeListener();
        sub.Pass.AddListener(listener);

        using (sub.BeginPass(_layout))
        {
        }
        using (sub.BeginPass(_layout))
        {
        }

        Assert.That(listener.BeginCount, Is.EqualTo(2));
        Assert.That(listener.EndCount, Is.EqualTo(2));
    }

    [Test(Description = "The render graph records every node's passes into the shared context and submits exactly once per frame")]
    public void GraphSubmitsOncePerFrame()
    {
        using var graph = new RenderGraph(_rendering, 32, 32, "test_graph");
        RenderGraphTexture a = graph.CreateTransient(new RenderGraphTextureDescriptor(_layout, name: "a"));
        RenderGraphTexture b = graph.CreateTransient(new RenderGraphTextureDescriptor(_layout, name: "b"));
        graph.Use(new PassNode(a, declaresOutput: false));
        graph.Use(new PassNode(b, declaresOutput: true));

        int before = _rendering.ScheduledSubmissionCount;
        graph.Execute(null);

        Assert.That(_rendering.ScheduledSubmissionCount, Is.EqualTo(before + 1),
            "Two pass nodes must share a single command buffer submission.");
    }

    [Test(Description = "A frame whose nodes record nothing still submits the shared buffer once")]
    public void GraphSubmitsEmptyFrameOnce()
    {
        using var graph = new RenderGraph(_rendering, 32, 32, "test_graph");
        RenderGraphTexture a = graph.CreateTransient(new RenderGraphTextureDescriptor(_layout, name: "a"));
        // Declares output but records nothing (compute recorded directly on the
        // shared command buffer cannot be counted, so the graph always submits).
        graph.Use(new NoopNode(a));

        int before = _rendering.ScheduledSubmissionCount;
        graph.Execute(null);

        Assert.That(_rendering.ScheduledSubmissionCount, Is.EqualTo(before + 1));
    }
}
