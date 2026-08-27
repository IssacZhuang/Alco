using NUnit.Framework;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.Rendering.Test;

/// <summary>
/// End-to-end tests of <see cref="RenderGraph"/> driven by the NoGPU backend:
/// execution order, culling, validation, transient lifetimes/aliasing, cross-frame
/// pool determinism, depth sharing, importing, batched submission, resize and
/// steady-state allocations.
/// </summary>
[TestFixture]
public sealed class TestRenderGraph
{
    /// <summary>
    /// A graph node whose per-frame declarations and Execute behavior are scripted
    /// through public fields. Records execution into a shared log (pre-sized and
    /// cleared by the test) so the per-frame path performs no managed allocations.
    /// </summary>
    private sealed class FakeNode : IRenderGraphNode
    {
        public readonly string Name;
        public bool IsEnabled { get; set; } = true;
        public bool DeclaresOutput;
        public RenderGraphTexture[] Reads = Array.Empty<RenderGraphTexture>();
        public RenderGraphTexture[] Writes = Array.Empty<RenderGraphTexture>();
        public RenderGraphTexture[] ReadWrites = Array.Empty<RenderGraphTexture>();
        public int ExecuteCount;
        public List<string>? Log;
        public Action<RenderGraphContext>? OnExecute;

        public FakeNode(string name)
        {
            Name = name;
        }

        public void Setup(RenderGraphBuilder builder)
        {
            for (int i = 0; i < Reads.Length; i++)
            {
                builder.Read(Reads[i]);
            }
            for (int i = 0; i < Writes.Length; i++)
            {
                builder.Write(Writes[i]);
            }
            for (int i = 0; i < ReadWrites.Length; i++)
            {
                builder.ReadWrite(ReadWrites[i]);
            }
            if (DeclaresOutput)
            {
                builder.ProducesOutput();
            }
        }

        public void Execute(in RenderGraphContext context)
        {
            ExecuteCount++;
            Log?.Add(Name);
            OnExecute?.Invoke(context);
        }
    }

    private DummyRenderingSystemHost _host;
    private RenderingSystem _rendering;
    private GPUDevice _device;

    private GPUAttachmentLayout _layoutColor;      // RGBA8Unorm, no depth
    private GPUAttachmentLayout _layoutFloat;      // RGBA16Float, no depth
    private GPUAttachmentLayout _layoutColorDepth; // RGBA8Unorm + Depth32Float
    private GPUAttachmentLayout _layoutFloatDepth; // RGBA16Float + Depth32Float

    [SetUp]
    public void SetUp()
    {
        _host = Utility.CreateRenderingSystem();
        _rendering = _host.RenderingSystem;
        _device = _rendering.GraphicsDevice;

        _layoutColor = _device.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            [new ColorAttachment(PixelFormat.RGBA8Unorm)], null, "graph_color"));
        _layoutFloat = _device.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            [new ColorAttachment(PixelFormat.RGBA16Float)], null, "graph_float"));
        _layoutColorDepth = _device.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            [new ColorAttachment(PixelFormat.RGBA8Unorm)], new DepthAttachment(PixelFormat.Depth32Float), "graph_color_depth"));
        _layoutFloatDepth = _device.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            [new ColorAttachment(PixelFormat.RGBA16Float)], new DepthAttachment(PixelFormat.Depth32Float), "graph_float_depth"));
    }

    [TearDown]
    public void TearDown()
    {
        _host.Dispose();
    }

    private RenderGraph CreateGraph(uint width = 32, uint height = 32)
    {
        return new RenderGraph(_rendering, width, height, "test_graph");
    }

    private static RenderGraphTextureDescriptor Describe(
        GPUAttachmentLayout layout,
        string name,
        uint width = 0,
        uint height = 0,
        RenderGraphTexture? depthSource = null)
    {
        return new RenderGraphTextureDescriptor(
            layout, width, height, 1.0f, depthSource, FilterMode.Linear, name);
    }

    [Test(Description = "Linear chain gbuffer -> lighting -> blit executes every node in registration order")]
    public void LinearChainExecutesInRegistrationOrder()
    {
        using RenderGraph graph = CreateGraph();
        RenderGraphTexture a = graph.CreateTransient(Describe(_layoutColor, "a"));
        RenderGraphTexture b = graph.CreateTransient(Describe(_layoutColor, "b"));

        var log = new List<string>(8);
        var gbuffer = new FakeNode("gbuffer") { Writes = [a], Log = log };
        var lighting = new FakeNode("lighting") { Reads = [a], Writes = [b], Log = log };
        var blit = new FakeNode("blit") { Reads = [b], DeclaresOutput = true, Log = log };
        graph.Use(gbuffer);
        graph.Use(lighting);
        graph.Use(blit);

        int submitted = graph.Execute(null);

        Assert.That(log, Is.EqualTo(new[] { "gbuffer", "lighting", "blit" }));
        Assert.That(gbuffer.ExecuteCount, Is.EqualTo(1));
        Assert.That(lighting.ExecuteCount, Is.EqualTo(1));
        Assert.That(blit.ExecuteCount, Is.EqualTo(1));
        Assert.That(submitted, Is.EqualTo(3));
    }

    [Test(Description = "A node whose writes are never read is culled; the rest of the chain runs")]
    public void UnreferencedWriteCullsTheWriter()
    {
        using RenderGraph graph = CreateGraph();
        RenderGraphTexture a = graph.CreateTransient(Describe(_layoutColor, "a"));
        RenderGraphTexture b = graph.CreateTransient(Describe(_layoutColor, "b"));

        var log = new List<string>(8);
        var dead = new FakeNode("dead") { Writes = [a], Log = log };
        var writer = new FakeNode("writer") { Writes = [b], Log = log };
        var blit = new FakeNode("blit") { Reads = [b], DeclaresOutput = true, Log = log };
        graph.Use(dead);
        graph.Use(writer);
        graph.Use(blit);

        graph.Execute(null);

        Assert.That(dead.ExecuteCount, Is.EqualTo(0));
        Assert.That(log, Is.EqualTo(new[] { "writer", "blit" }));
    }

    [Test(Description = "Disabling the chain tail keeps the content root alive and skips the disabled node")]
    public void DisabledChainTailKeepsContentRootAlive()
    {
        using RenderGraph graph = CreateGraph();
        RenderGraphTexture a = graph.CreateTransient(Describe(_layoutColor, "a"));
        RenderGraphTexture b = graph.CreateTransient(Describe(_layoutColor, "b"));

        var log = new List<string>(8);
        var gbuffer = new FakeNode("gbuffer") { Writes = [a], Log = log };
        var lighting = new FakeNode("lighting") { Reads = [a], Writes = [b], DeclaresOutput = true, Log = log };
        var post = new FakeNode("post") { Reads = [b], DeclaresOutput = true, IsEnabled = false, Log = log };
        graph.Use(gbuffer);
        graph.Use(lighting);
        graph.Use(post);

        graph.Execute(null);

        Assert.That(post.ExecuteCount, Is.EqualTo(0));
        Assert.That(log, Is.EqualTo(new[] { "gbuffer", "lighting" }));
    }

    [Test(Description = "A conditionally read shadow map keeps its producer alive only while actually read")]
    public void ConditionalReadTogglesProducerCulling()
    {
        using RenderGraph graph = CreateGraph();
        RenderGraphTexture shadowMap = graph.CreateTransient(Describe(_layoutColor, "shadowMap"));
        RenderGraphTexture a = graph.CreateTransient(Describe(_layoutColor, "a"));
        RenderGraphTexture b = graph.CreateTransient(Describe(_layoutColor, "b"));

        var log = new List<string>(8);
        var shadow = new FakeNode("shadow") { Writes = [shadowMap], Log = log };
        var gbuffer = new FakeNode("gbuffer") { Writes = [a], Log = log };
        var lighting = new FakeNode("lighting") { Reads = [a, shadowMap], Writes = [b], Log = log };
        var blit = new FakeNode("blit") { Reads = [b], DeclaresOutput = true, Log = log };
        RenderGraphTexture[] withShadow = [a, shadowMap];
        RenderGraphTexture[] withoutShadow = [a];
        graph.Use(shadow);
        graph.Use(gbuffer);
        graph.Use(lighting);
        graph.Use(blit);

        // Frame 1: lighting reads the shadow map -> the shadow pass survives.
        lighting.Reads = withShadow;
        graph.Execute(null);
        Assert.That(log, Is.EqualTo(new[] { "shadow", "gbuffer", "lighting", "blit" }));
        Assert.That(shadow.ExecuteCount, Is.EqualTo(1));

        // Frame 2: lighting no longer reads the shadow map -> the shadow pass is culled.
        lighting.Reads = withoutShadow;
        log.Clear();
        graph.Execute(null);
        Assert.That(log, Is.EqualTo(new[] { "gbuffer", "lighting", "blit" }));
        Assert.That(shadow.ExecuteCount, Is.EqualTo(1));
        Assert.That(lighting.ExecuteCount, Is.EqualTo(2));
    }

    [Test(Description = "A ReadWrite (in-place) chain propagates the dependency and keeps every node alive")]
    public void ReadWriteChainKeepsEveryNodeAlive()
    {
        using RenderGraph graph = CreateGraph();
        RenderGraphTexture sceneColor = graph.CreateTransient(Describe(_layoutFloat, "sceneColor"));

        var log = new List<string>(8);
        var lighting = new FakeNode("lighting") { Writes = [sceneColor], Log = log };
        var forward = new FakeNode("forward") { ReadWrites = [sceneColor], Log = log };
        var blit = new FakeNode("blit") { Reads = [sceneColor], DeclaresOutput = true, Log = log };
        graph.Use(lighting);
        graph.Use(forward);
        graph.Use(blit);

        graph.Execute(null);

        Assert.That(log, Is.EqualTo(new[] { "lighting", "forward", "blit" }));
    }

    [Test(Description = "Reading a transient before any enabled earlier node writes it throws with node and resource names")]
    public void ReadBeforeWriteThrows()
    {
        using RenderGraph graph = CreateGraph();
        RenderGraphTexture a = graph.CreateTransient(Describe(_layoutColor, "a"));

        var reader = new FakeNode("reader") { Reads = [a] };
        var writer = new FakeNode("writer") { Writes = [a], DeclaresOutput = true };
        graph.Use(reader);
        graph.Use(writer);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => graph.Execute(null));
        Assert.That(exception.Message, Does.Contain(nameof(FakeNode)));
        Assert.That(exception.Message, Does.Contain("'a'"));
        Assert.That(reader.ExecuteCount, Is.EqualTo(0));
        Assert.That(writer.ExecuteCount, Is.EqualTo(0));
    }

    [Test(Description = "Transients with equal specs and non-overlapping lifetimes alias the same pooled texture; overlapping lifetimes do not")]
    public void NonOverlappingLifetimesAlias()
    {
        // Graph 1: A is touched by n1..n2, B by n3..n4 — non-overlapping lifetimes,
        // both resources created up front before any Execute.
        using RenderGraph graph = CreateGraph();
        RenderGraphTexture a = graph.CreateTransient(Describe(_layoutColor, "a"));
        RenderGraphTexture b = graph.CreateTransient(Describe(_layoutColor, "b"));
        graph.Use(new FakeNode("n1") { Writes = [a] });
        graph.Use(new FakeNode("n2") { Reads = [a], DeclaresOutput = true });
        graph.Use(new FakeNode("n3") { Writes = [b] });
        graph.Use(new FakeNode("n4") { Reads = [b], DeclaresOutput = true });
        graph.Execute(null);

        Assert.That(
            ReferenceEquals(a.Texture.FrameBuffer.Colors[0], b.Texture.FrameBuffer.Colors[0]),
            Is.True,
            "Non-overlapping equal-spec transients should alias the same pooled texture.");

        // Graph 2: A2 [n1..n3] overlaps B2 [n2..n3] — both are live while m3 executes,
        // so they must be backed by different textures.
        using RenderGraph overlapGraph = CreateGraph();
        RenderGraphTexture a2 = overlapGraph.CreateTransient(Describe(_layoutColor, "a2"));
        RenderGraphTexture b2 = overlapGraph.CreateTransient(Describe(_layoutColor, "b2"));
        var m1 = new FakeNode("m1") { Writes = [a2] };
        var m2 = new FakeNode("m2") { Writes = [b2] };
        var m3 = new FakeNode("m3") { Reads = [a2, b2], DeclaresOutput = true };
        overlapGraph.Use(m1);
        overlapGraph.Use(m2);
        overlapGraph.Use(m3);
        overlapGraph.Execute(null);

        Assert.That(
            ReferenceEquals(a2.Texture.FrameBuffer.Colors[0], b2.Texture.FrameBuffer.Colors[0]),
            Is.False,
            "Overlapping transients must not alias the same pooled texture.");
    }

    [Test(Description = "Regression: transients created up front (before any Execute) with non-overlapping lifetimes alias the same pooled attachment")]
    public void UpFrontCreatedTransientsAlias()
    {
        using RenderGraph graph = CreateGraph();
        RenderGraphTexture a = graph.CreateTransient(Describe(_layoutColor, "a"));
        RenderGraphTexture b = graph.CreateTransient(Describe(_layoutColor, "b"));
        graph.Use(new FakeNode("n1") { Writes = [a] });
        graph.Use(new FakeNode("n2") { Reads = [a], DeclaresOutput = true });
        graph.Use(new FakeNode("n3") { Writes = [b] });
        graph.Use(new FakeNode("n4") { Reads = [b], DeclaresOutput = true });

        graph.Execute(null);

        var colorA = a.ColorAttachments ?? throw new InvalidOperationException("unexpected null");
        var colorB = b.ColorAttachments ?? throw new InvalidOperationException("unexpected null");
        Assert.That(
            ReferenceEquals(colorA[0], colorB[0]),
            Is.True,
            "A's lifetime ends before B's starts: B must be assigned the pooled attachment A released.");

        // The shared assignment is stable: frame 2 reassigns the identical
        // attachment through the sticky path without rebinding either facade.
        uint versionA = a.Texture.Version;
        uint versionB = b.Texture.Version;
        graph.Execute(null);
        Assert.That(ReferenceEquals(colorA[0], colorB[0]), Is.True);
        Assert.That(a.Texture.Version, Is.EqualTo(versionA), "Steady state must not rebind A.");
        Assert.That(b.Texture.Version, Is.EqualTo(versionB), "Steady state must not rebind B.");
    }

    [Test(Description = "Regression: nested lifetimes keep stable dedicated assignments across frames (no rebind oscillation)")]
    public void NestedLifetimesKeepStableAssignments()
    {
        // A = [n1..n4], B = [n2..n3]: B's lifetime is nested inside A's, so both
        // need dedicated textures and the assignment must be stable across frames.
        using RenderGraph graph = CreateGraph();
        RenderGraphTexture a = graph.CreateTransient(Describe(_layoutColor, "a"));
        RenderGraphTexture b = graph.CreateTransient(Describe(_layoutColor, "b"));
        graph.Use(new FakeNode("n1") { Writes = [a] });
        graph.Use(new FakeNode("n2") { Writes = [b] });
        graph.Use(new FakeNode("n3") { Reads = [b], DeclaresOutput = true });
        graph.Use(new FakeNode("n4") { Reads = [a], DeclaresOutput = true });

        graph.Execute(null);
        uint versionA1 = a.Texture.Version;
        uint versionB1 = b.Texture.Version;
        graph.Execute(null);
        uint versionA2 = a.Texture.Version;
        uint versionB2 = b.Texture.Version;
        graph.Execute(null);

        var colorA = a.ColorAttachments ?? throw new InvalidOperationException("unexpected null");
        var colorB = b.ColorAttachments ?? throw new InvalidOperationException("unexpected null");
        Assert.That(
            ReferenceEquals(colorA[0], colorB[0]),
            Is.False,
            "Nested lifetimes overlap and must keep dedicated pooled attachments.");
        Assert.That(versionA2, Is.EqualTo(versionA1), "Frame 2 must not rebind A.");
        Assert.That(versionB2, Is.EqualTo(versionB1), "Frame 2 must not rebind B.");
        Assert.That(a.Texture.Version, Is.EqualTo(versionA2), "Frame 3 must not rebind A.");
        Assert.That(b.Texture.Version, Is.EqualTo(versionB2), "Frame 3 must not rebind B.");
    }

    [Test(Description = "The identical schedule across frames keeps the identical backing without rebinding the facade")]
    public void IdenticalScheduleKeepsIdenticalBackingAcrossFrames()
    {
        using RenderGraph graph = CreateGraph();
        RenderGraphTexture a = graph.CreateTransient(Describe(_layoutColor, "a"));
        var writer = new FakeNode("writer") { Writes = [a] };
        var blit = new FakeNode("blit") { Reads = [a], DeclaresOutput = true };
        graph.Use(writer);
        graph.Use(blit);

        graph.Execute(null);
        GPUFrameBuffer frameBuffer = a.Texture.FrameBuffer;
        uint version = a.Texture.Version;

        graph.Execute(null);

        Assert.That(ReferenceEquals(a.Texture.FrameBuffer, frameBuffer), Is.True, "The facade must not be rebound in steady state.");
        Assert.That(a.Texture.Version, Is.EqualTo(version), "No rebind means no version bump in steady state.");
    }

    [Test(Description = "A transient sharing another transient's depth gets the identical depth attachment; an unwritten depth source throws")]
    public void DepthSourceSharing()
    {
        using RenderGraph graph = CreateGraph();
        RenderGraphTexture gbuffer = graph.CreateTransient(Describe(_layoutFloatDepth, "gbuffer"));
        RenderGraphTexture sceneColor = graph.CreateTransient(Describe(_layoutColorDepth, "sceneColor", 0, 0, gbuffer));

        var geometry = new FakeNode("geometry") { Writes = [gbuffer] };
        var forward = new FakeNode("forward") { Writes = [sceneColor], DeclaresOutput = true };
        graph.Use(geometry);
        graph.Use(forward);

        graph.Execute(null);

        Assert.That(gbuffer.Texture.FrameBuffer.DepthStencil, Is.Not.Null);
        Assert.That(
            ReferenceEquals(gbuffer.Texture.FrameBuffer.DepthStencil, sceneColor.Texture.FrameBuffer.DepthStencil),
            Is.True,
            "The dependent resource must share the depth attachment instance of its source.");

        // A graph where nobody writes the depth source before the dependent writer
        // must fail validation.
        using RenderGraph invalidGraph = CreateGraph();
        RenderGraphTexture orphanDepth = invalidGraph.CreateTransient(Describe(_layoutFloatDepth, "orphanDepth"));
        RenderGraphTexture dependent = invalidGraph.CreateTransient(Describe(_layoutColorDepth, "dependent", 0, 0, orphanDepth));
        var lonely = new FakeNode("lonely") { Writes = [dependent], DeclaresOutput = true };
        invalidGraph.Use(lonely);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => invalidGraph.Execute(null));
        Assert.That(exception.Message, Does.Contain("'dependent'"));
        Assert.That(exception.Message, Does.Contain("'orphanDepth'"));
    }

    [Test(Description = "An imported render texture is never pooled, materializes nothing and is never disposed by the graph")]
    public void ImportedTextureIsNeverPooled()
    {
        using RenderGraph graph = CreateGraph();
        RenderGraphTexture a = graph.CreateTransient(Describe(_layoutColor, "a"));
        using RenderTexture external = _rendering.CreateRenderTexture(_layoutColor, 16, 16, "external");
        RenderGraphTexture imported = graph.Import(external);
        var writer = new FakeNode("writer") { Writes = [a] };
        var blit = new FakeNode("blit") { Reads = [a, imported], DeclaresOutput = true };
        graph.Use(writer);
        graph.Use(blit);

        int pooledBefore = graph.PooledTextureCount;
        graph.Execute(null);
        graph.Execute(null);
        graph.Execute(null);

        Assert.That(imported.IsImported, Is.True);
        Assert.That(ReferenceEquals(imported.Texture, external), Is.True);
        Assert.That(pooledBefore, Is.EqualTo(1), "Only the transient materializes a pooled texture; the import contributes none.");
        Assert.That(graph.PooledTextureCount, Is.EqualTo(pooledBefore),
            "Steady-state frames must not materialize new pooled textures.");
        Assert.That(blit.ExecuteCount, Is.EqualTo(3));

        graph.Dispose();
        Assert.That(external.IsDisposed, Is.False, "The graph must not dispose imported textures.");
    }

    [Test(Description = "Execute returns the number of alive nodes that executed this frame")]
    public void ExecuteReturnsExecutedNodeCount()
    {
        using RenderGraph graph = CreateGraph();
        RenderGraphTexture a = graph.CreateTransient(Describe(_layoutColor, "a"));

        bool recordWork = true;
        var node = new FakeNode("recorder")
        {
            Writes = [a],
            DeclaresOutput = true,
            OnExecute = context =>
            {
                if (!recordWork)
                {
                    return;
                }
                using (context.RenderContext.BeginPass(a.Texture.FrameBuffer))
                {
                }
            },
        };
        graph.Use(node);

        int withWork = graph.Execute(null);
        Assert.That(withWork, Is.EqualTo(1));
    }

    [Test(Description = "Resize rematerializes graph-relative transients at the new size and later frames still run")]
    public void ResizeRematerializesGraphRelativeTransients()
    {
        using RenderGraph graph = CreateGraph(32, 32);
        RenderGraphTexture a = graph.CreateTransient(Describe(_layoutColor, "a"));
        var node = new FakeNode("node") { Writes = [a], DeclaresOutput = true };
        graph.Use(node);

        graph.Execute(null);
        Assert.That(a.Texture.Width, Is.EqualTo(32));
        Assert.That(a.Texture.Height, Is.EqualTo(32));

        Assert.DoesNotThrow(() => graph.Resize(64, 64));

        Assert.That(graph.Width, Is.EqualTo(64));
        Assert.That(graph.Height, Is.EqualTo(64));
        Assert.That(a.Width, Is.EqualTo(64));
        Assert.That(a.Height, Is.EqualTo(64));
        Assert.That(a.Texture.Width, Is.EqualTo(64), "The facade must be rematerialized at the new size.");
        Assert.That(a.Texture.Height, Is.EqualTo(64));

        Assert.DoesNotThrow(() => graph.Execute(null));
        Assert.That(node.ExecuteCount, Is.EqualTo(2));
    }

    [Test(Description = "Resize prunes pool entries of the old size; absolute-size transients keep their backing")]
    public void ResizePrunesStaleSizePoolEntries()
    {
        using RenderGraph graph = CreateGraph(32, 32);
        RenderGraphTexture relative = graph.CreateTransient(Describe(_layoutColor, "relative"));
        RenderGraphTexture absolute = graph.CreateTransient(Describe(_layoutColor, "absolute", width: 16, height: 16));
        var node = new FakeNode("node") { Writes = [relative, absolute], DeclaresOutput = true };
        graph.Use(node);

        graph.Execute(null);
        int pooledBefore = graph.PooledTextureCount;
        GPUTexture absoluteBacking = absolute.Texture.FrameBuffer.Colors[0];

        graph.Resize(64, 64);

        Assert.That(graph.PooledTextureCount, Is.EqualTo(pooledBefore),
            "The old-size entry is pruned and the new-size entry materialized; the absolute-size entry is kept.");
        Assert.That(ReferenceEquals(absolute.Texture.FrameBuffer.Colors[0], absoluteBacking), Is.True,
            "The absolute-size transient keeps its pooled texture across the resize.");

        Assert.DoesNotThrow(() => graph.Execute(null));
        Assert.That(node.ExecuteCount, Is.EqualTo(2));
    }

    [Test(Description = "Steady-state frames perform no meaningful managed allocations on the Setup/Compile/Execute path")]
    public void SteadyStateFramesDoNotAllocate()
    {
        using RenderGraph graph = CreateGraph();
        RenderGraphTexture a = graph.CreateTransient(Describe(_layoutColor, "a"));
        RenderGraphTexture b = graph.CreateTransient(Describe(_layoutColor, "b"));
        var gbuffer = new FakeNode("gbuffer") { Writes = [a] };
        var lighting = new FakeNode("lighting") { Reads = [a], Writes = [b] };
        var blit = new FakeNode("blit") { Reads = [b], DeclaresOutput = true };
        graph.Use(gbuffer);
        graph.Use(lighting);
        graph.Use(blit);

        for (int i = 0; i < 5; i++)
        {
            graph.Execute(null);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        int submitted = 0;
        for (int i = 0; i < 100; i++)
        {
            submitted += graph.Execute(null);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.That(submitted, Is.EqualTo(300));
        Assert.That(allocated, Is.LessThan(32 * 1024),
            $"Steady-state Execute allocated {allocated} bytes over 100 frames.");
        TestContext.Out.WriteLine($"Steady-state allocation over 100 frames: {allocated} bytes.");
    }

    [Test(Description = "Regression: DestroyTransient disposes the facade but must not dispose the pooled backing; a later same-spec transient receives the intact pooled attachment")]
    public void DestroyTransientKeepsPooledAttachmentAlive()
    {
        using RenderGraph graph = CreateGraph();
        RenderGraphTexture a = graph.CreateTransient(Describe(_layoutColor, "a"));

        // One frame returns the occupied entry to the idle set.
        graph.Execute(null);

        GPUTexture pooledTexture = a.Texture.FrameBuffer.Colors[0];
        GPUTextureView pooledView = a.Texture.FrameBuffer.ColorViews[0];

        // The SSR resolution-slider path between frames: destroy the old transient.
        // The facade goes away, the pooled backing must stay alive for reuse.
        graph.DestroyTransient(a);

        Assert.That(a.Texture.IsDisposed, Is.True, "The facade itself is disposed.");
        Assert.That(pooledTexture.IsDisposed, Is.False, "The pooled texture belongs to the pool, not to the facade.");
        Assert.That(pooledView.IsDisposed, Is.False, "The pooled view belongs to the pool, not to the facade.");

        // A later same-spec transient (the slider dragged back, or another node with
        // an equal key) takes the idle entry and must find it intact.
        RenderGraphTexture b = graph.CreateTransient(Describe(_layoutColor, "b"));

        Assert.That(ReferenceEquals(b.Texture.FrameBuffer.Colors[0], pooledTexture), Is.True,
            "The idle pooled attachment is reused by the same-spec transient.");
        Assert.That(ReferenceEquals(b.Texture.FrameBuffer.ColorViews[0], pooledView), Is.True);
        Assert.That(pooledTexture.IsDisposed, Is.False);
        Assert.That(pooledView.IsDisposed, Is.False);

        // Record a real pass into the reused backing: the frame buffer composes the
        // pooled views, which must still be valid at render pass creation.
        var node = new FakeNode("node")
        {
            Writes = [b],
            DeclaresOutput = true,
            OnExecute = context =>
            {
                using (context.RenderContext.BeginPass(b.Texture.FrameBuffer))
                {
                }
            },
        };
        graph.Use(node);

        Assert.DoesNotThrow(() => graph.Execute(null));
        Assert.That(node.ExecuteCount, Is.EqualTo(1));
    }

    [Test(Description = "The color texture wrappers of a render texture do not own the attachments: disposing the render texture disposes the wrappers and the frame buffer, while attachment release is left to the frame buffer's (deferred) destruction")]
    public void RenderTextureWrappersDoNotOwnAttachments()
    {
        RenderTexture renderTexture = _rendering.CreateRenderTexture(_layoutColor, 16, 16, "owned");
        Texture2D wrapper = renderTexture.ColorTextures[0];
        GPUTexture texture = renderTexture.FrameBuffer.Colors[0];
        GPUTextureView view = renderTexture.FrameBuffer.ColorViews[0];

        renderTexture.Dispose();

        Assert.That(wrapper.IsDisposed, Is.True, "The wrapper itself is disposed.");
        Assert.That(renderTexture.FrameBuffer.IsDisposed, Is.True, "The frame buffer is disposed with the render texture.");
        Assert.That(texture.IsDisposed, Is.False,
            "The attachment must not be released synchronously by the wrapper; the frame buffer releases it on destruction.");
        Assert.That(view.IsDisposed, Is.False,
            "The attachment view must not be released synchronously by the wrapper; the frame buffer releases it on destruction.");
    }
}
