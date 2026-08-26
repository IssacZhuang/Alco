using NUnit.Framework;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.Rendering.Test;

/// <summary>
/// Tests of the RAII scopes of <see cref="RenderContext"/>: frame scopes
/// (<see cref="RenderFrameScope"/>, the only submitter) and pass scopes
/// (<see cref="RenderPassScope"/>) handed out by
/// <see cref="RenderContext.BeginPass(GPUFrameBuffer, ReadOnlySpan{ClearColorData}, float?, uint?, ReadOnlySpan{AttachmentOps}, AttachmentOps?)"/>
/// and <see cref="SubRenderContext.BeginPass"/>: frame-only submission, misuse
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

    [Test(Description = "BeginPass without an open frame scope throws InvalidOperationException — the frame scope is the only submitter")]
    public void BeginPassWithoutFrameThrows()
    {
        using RenderContext context = _rendering.CreateRenderContext("test");

        Assert.Throws<InvalidOperationException>(() => context.BeginPass(_target.FrameBuffer));
        Assert.That(context.IsPassOpen, Is.False);
    }

    [Test(Description = "Two sequential frames on one context submit once each")]
    public void SequentialFramesSubmitOnceEach()
    {
        using RenderContext context = _rendering.CreateRenderContext("test");
        int before = _rendering.ScheduledSubmissionCount;

        using (context.BeginFrame())
        {
            using (context.BeginPass(_target.FrameBuffer))
            {
            }
        }
        using (context.BeginFrame())
        {
            using (context.BeginPass(_target.FrameBuffer))
            {
            }
        }

        Assert.That(_rendering.ScheduledSubmissionCount, Is.EqualTo(before + 2));
    }

    [Test(Description = "Calls on a disposed scope throw InvalidOperationException")]
    public void CallsOnClosedScopeThrow()
    {
        using RenderContext context = _rendering.CreateRenderContext("test");
        using RenderFrameScope frame = context.BeginFrame();
        RenderPassScope pass = context.BeginPass(_target.FrameBuffer);
        pass.Dispose();

        Assert.Throws<InvalidOperationException>(() => pass.SetStencilReference(1));
        Assert.Throws<InvalidOperationException>(() => pass.Dispose());
    }

    [Test(Description = "Beginning a second pass while one is open throws InvalidOperationException")]
    public void NestedBeginPassThrows()
    {
        using RenderContext context = _rendering.CreateRenderContext("test");
        using (context.BeginFrame())
        using (context.BeginPass(_target.FrameBuffer))
        {
            Assert.Throws<InvalidOperationException>(() => context.BeginPass(_target.FrameBuffer));
        }
    }

    [Test(Description = "Listeners are notified once per frame scope (one pass per frame here), on the recycled scope identity")]
    public void ListenersFireOncePerFrame()
    {
        using RenderContext context = _rendering.CreateRenderContext("test");
        var listener = new FakeListener();
        context.Pass.AddListener(listener);

        using (context.BeginFrame())
        {
            using (context.BeginPass(_target.FrameBuffer))
            {
            }
        }
        using (context.BeginFrame())
        {
            using (context.BeginPass(_target.FrameBuffer))
            {
            }
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
        using (RenderFrameScope frame = context.BeginFrame())
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

    [Test(Description = "A frame scope records many passes into one buffer and submits once on dispose (shared mode outside the render graph)")]
    public void FrameScopeSubmitsOnceForManyPasses()
    {
        using RenderContext context = _rendering.CreateRenderContext("test");
        int before = _rendering.ScheduledSubmissionCount;

        using (RenderFrameScope frame = context.BeginFrame())
        {
            using (context.BeginPass(_target.FrameBuffer))
            {
            }
            Assert.That(_rendering.ScheduledSubmissionCount, Is.EqualTo(before),
                "A pass inside a frame scope must not submit.");
            using (context.BeginPass(_target.FrameBuffer))
            {
            }
        }

        Assert.That(_rendering.ScheduledSubmissionCount, Is.EqualTo(before + 1),
            "The frame scope submits the shared buffer exactly once.");

        // After the frame, the next recording requires its own frame scope.
        using (context.BeginFrame())
        {
            using (context.BeginPass(_target.FrameBuffer))
            {
            }
        }
        Assert.That(_rendering.ScheduledSubmissionCount, Is.EqualTo(before + 2));
    }

    [Test(Description = "Listeners fire once per frame scope (buffer cycle), not per pass")]
    public void FrameScopeListenersFireOncePerFrame()
    {
        using RenderContext context = _rendering.CreateRenderContext("test");
        var listener = new FakeListener();
        context.Pass.AddListener(listener);

        using (context.BeginFrame())
        {
            using (context.BeginPass(_target.FrameBuffer))
            {
            }
            using (context.BeginPass(_target.FrameBuffer))
            {
            }
        }

        Assert.That(listener.BeginCount, Is.EqualTo(1));
        Assert.That(listener.EndCount, Is.EqualTo(1));
    }

    [Test(Description = "Disposing a frame scope with a pass still open discards the buffer without submitting and leaves the context reusable")]
    public void FrameScopeWithOpenPassAbortsWithoutSubmit()
    {
        using RenderContext context = _rendering.CreateRenderContext("test");
        int before = _rendering.ScheduledSubmissionCount;

        using (RenderFrameScope frame = context.BeginFrame())
        {
            context.BeginPass(_target.FrameBuffer);
        } // pass still open here → abort, no submission

        Assert.That(_rendering.ScheduledSubmissionCount, Is.EqualTo(before));
        Assert.That(context.IsPassOpen, Is.False);

        // Clean state: the next frame records and submits normally.
        using (context.BeginFrame())
        {
            using (context.BeginPass(_target.FrameBuffer))
            {
            }
        }
        Assert.That(_rendering.ScheduledSubmissionCount, Is.EqualTo(before + 1));
    }

    [Test(Description = "Nested BeginFrame or a double frame dispose throws InvalidOperationException")]
    public void FrameScopeNestingAndDoubleDisposeThrow()
    {
        using RenderContext context = _rendering.CreateRenderContext("test");
        RenderFrameScope frame = context.BeginFrame();

        Assert.Throws<InvalidOperationException>(() => context.BeginFrame());

        frame.Dispose();
        Assert.Throws<InvalidOperationException>(() => frame.Dispose());
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
