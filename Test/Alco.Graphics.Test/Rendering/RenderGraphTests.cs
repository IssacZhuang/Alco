// CPU compilation/culling/aliasing/barrier tests and headless GPU validation tests for RenderGraph and RenderGraphLabScene.

using Alco.Graphics;
using Alco.Graphics.LowLevel;
using Buffer = Alco.Graphics.Buffer;

internal static unsafe class RenderGraphTests
{
    public static void RunCpu(Action<bool, string> check)
    {
        Console.WriteLine("RenderGraph (CPU):");
        TestNodeRegistration(check);
        TestCulling(check);
        TestValidation(check);
        TestAliasingAndPooling(check);
        TestBarrierPlanning(check);
        TestZeroSteadyStateAllocations(check);
    }

    private static bool IsPassCulled(RenderGraph graph, string passName)
    {
        ReadOnlySpan<RenderGraphPassReport> reports = graph.PassReports;
        for (int i = 0; i < reports.Length; ++i)
        {
            if (reports[i].Name == passName)
                return !reports[i].Alive;
        }
        return false;
    }

    private static void TestNodeRegistration(Action<bool, string> check)
    {
        RenderGraph graph = new(new Extent2D(128, 128), "RegGraph");
        NodeA a = new();
        NodeB b = new();
        NodeC c = new();
        NodeD d = new();

        graph.Use(a).Use(c).InsertBefore<NodeC>(b).InsertAfter<NodeC>(d);
        check(graph.Nodes.Count == 4 && graph.Nodes[0] == a && graph.Nodes[1] == b && graph.Nodes[2] == c && graph.Nodes[3] == d,
            "Use / InsertBefore / InsertAfter preserve exact node order");
        check(graph.Get<NodeB>() == b, "Get<T> returns matching node");
        check(graph.Remove(b) && graph.Nodes.Count == 3 && graph.Get<NodeB>() == null, "Remove detaches node cleanly");

        bool missingAnchorThrew = false;
        try { graph.InsertBefore<NodeB>(new NodeD()); }
        catch (InvalidOperationException) { missingAnchorThrew = true; }
        check(missingAnchorThrew, "InsertBefore with missing anchor throws InvalidOperationException");

        graph.Destroy();
    }

    private static void TestCulling(Action<bool, string> check)
    {
        RenderGraph graph = new(new Extent2D(256, 256), "CullGraph");
        RGTexture texA = graph.CreateTransientTexture(new RGTextureDesc { Name = "texA", Format = Format.Rgba8Unorm });
        RGTexture texB = graph.CreateTransientTexture(new RGTextureDesc { Name = "texB", Format = Format.Rgba8Unorm });
        RGTexture unused = graph.CreateTransientTexture(new RGTextureDesc { Name = "unused", Format = Format.Rgba8Unorm });
        bool readTexB = true;

        graph.Use(new LambdaNode(b =>
        {
            RenderGraphPassBuilder p0 = b.AddPass("UnusedProducer", NullNode.Instance);
            p0.Write(unused, RGUsage.ColorAttachmentWrite);

            RenderGraphPassBuilder p1 = b.AddPass("SubPass_Raw", NullNode.Instance);
            RGTexture raw = b.CreateTexture(new RGTextureDesc { Name = "raw", Format = Format.Rgba8Unorm });
            p1.Write(raw, RGUsage.ComputeWrite);

            RenderGraphPassBuilder p2 = b.AddPass("SubPass_Blur", NullNode.Instance);
            p2.Read(raw, RGUsage.ComputeRead);
            p2.Write(texB, RGUsage.ComputeWrite);

            RenderGraphPassBuilder p3 = b.AddPass("MainScene", NullNode.Instance);
            p3.Write(texA, RGUsage.ColorAttachmentWrite);

            RenderGraphPassBuilder p4 = b.AddPass("Composite", NullNode.Instance);
            p4.Read(texA, RGUsage.PixelRead);
            if (readTexB)
                p4.Read(texB, RGUsage.PixelRead);
            if (b.HasOutput)
            {
                p4.Write(b.Output, RGUsage.ColorAttachmentWrite);
                p4.ProducesOutput();
            }
        }));

        RenderGraphTarget target = RenderGraphTarget.CreateDryRun(new Extent2D(256, 256), Format.Bgra8Srgb);
        int aliveWithB = graph.CompileDryRun(target);
        check(aliveWithB == 4 && IsPassCulled(graph, "UnusedProducer") && !IsPassCulled(graph, "SubPass_Raw") && !IsPassCulled(graph, "SubPass_Blur"),
            "Dead-pass culling removes unreferenced UnusedProducer while keeping reachable chain alive");

        readTexB = false;
        int aliveWithoutB = graph.CompileDryRun(target);
        check(aliveWithoutB == 2 && IsPassCulled(graph, "SubPass_Raw") && IsPassCulled(graph, "SubPass_Blur"),
            "Disabling downstream read cascades culling through both multi-pass producers");

        int aliveHeadless = graph.CompileDryRun(default);
        check(aliveHeadless == 0, "Headless frame without ProducesOutput or FinalUsage culls all passes");

        // Dead-store overwrite culling: Pass 0 writes texA, Pass 1 overwrites texA (Write, not ReadWrite), Pass 2 outputs texA.
        RenderGraph overwriteGraph = new(new Extent2D(64, 64), "OverwriteGraph");
        RGTexture color = overwriteGraph.CreateTransientTexture(new RGTextureDesc { Name = "color", Format = Format.Rgba8Unorm });
        bool useReadWrite = false;
        overwriteGraph.Use(new LambdaNode(b =>
        {
            RenderGraphPassBuilder p0 = b.AddPass("FirstWrite", NullNode.Instance);
            p0.Write(color, RGUsage.ColorAttachmentWrite);

            RenderGraphPassBuilder p1 = b.AddPass("SecondWrite", NullNode.Instance);
            if (useReadWrite)
                p1.ReadWrite(color, RGUsage.ColorAttachment);
            else
                p1.Write(color, RGUsage.ColorAttachmentWrite);

            RenderGraphPassBuilder p2 = b.AddPass("Present", NullNode.Instance);
            p2.Read(color, RGUsage.PixelRead);
            p2.Write(b.Output, RGUsage.ColorAttachmentWrite);
            p2.ProducesOutput();
        }));
        overwriteGraph.CompileDryRun(target);
        check(IsPassCulled(overwriteGraph, "FirstWrite"), "Pure Write overwrite eliminates dead earlier store");
        useReadWrite = true;
        overwriteGraph.CompileDryRun(target);
        check(!IsPassCulled(overwriteGraph, "FirstWrite"), "ReadWrite preserves earlier writer");

        overwriteGraph.Destroy();
        graph.Destroy();
    }

    private static void TestValidation(Action<bool, string> check)
    {
        RenderGraphTarget target = RenderGraphTarget.CreateDryRun(new Extent2D(128, 128), Format.Bgra8Srgb);

        // 1. Read before write on transient.
        RenderGraph g1 = new(new Extent2D(128, 128), "ValReadBeforeWrite");
        RGTexture t1 = g1.CreateTransientTexture(new RGTextureDesc { Name = "unwritten_tex", Format = Format.Rgba8Unorm });
        g1.Use(new LambdaNode(b =>
        {
            RenderGraphPassBuilder p = b.AddPass("BadReader", NullNode.Instance);
            p.Read(t1, RGUsage.PixelRead);
            p.ProducesOutput();
        }));
        bool caughtReadBeforeWrite = false;
        try { g1.CompileDryRun(target); }
        catch (InvalidOperationException ex) { caughtReadBeforeWrite = ex.Message.Contains("BadReader") && ex.Message.Contains("unwritten_tex"); }
        check(caughtReadBeforeWrite, "Validation rejects reading transient before write with pass and resource names");
        g1.Destroy();

        // 2. Stale frame handle reused next frame.
        RenderGraph g2 = new(new Extent2D(128, 128), "ValStale");
        RGTexture leakedHandle = default;
        g2.Use(new LambdaNode(b =>
        {
            if (!leakedHandle.IsValid)
            {
                leakedHandle = b.CreateTexture(new RGTextureDesc { Name = "frame_only", Format = Format.Rgba8Unorm });
                RenderGraphPassBuilder p = b.AddPass("Frame0", NullNode.Instance);
                p.Write(leakedHandle, RGUsage.ColorAttachmentWrite);
                p.ProducesOutput();
            }
            else
            {
                RenderGraphPassBuilder p = b.AddPass("Frame1", NullNode.Instance);
                p.Write(leakedHandle, RGUsage.ColorAttachmentWrite);
                p.ProducesOutput();
            }
        }));
        g2.CompileDryRun(target);
        bool caughtStale = false;
        try { g2.CompileDryRun(target); }
        catch (InvalidOperationException) { caughtStale = true; }
        check(caughtStale, "Validation rejects stale frame-scoped handle reused on next frame");
        g2.Destroy();

        // 3. Released retained handle rejected.
        RenderGraph g3 = new(new Extent2D(128, 128), "ValReleased");
        RGTexture released = g3.CreateTransientTexture(new RGTextureDesc { Name = "released", Format = Format.Rgba8Unorm });
        g3.ReleaseResource(released);
        g3.Use(new LambdaNode(b =>
        {
            RenderGraphPassBuilder p = b.AddPass("UseReleased", NullNode.Instance);
            p.Write(released, RGUsage.ColorAttachmentWrite);
        }));
        bool caughtReleased = false;
        try { g3.CompileDryRun(target); }
        catch (InvalidOperationException) { caughtReleased = true; }
        check(caughtReleased, "Validation rejects released retained handle");
        g3.Destroy();

        // 4. Incompatible explicit TextureUsage rejected and SupportsUsage reports capabilities accurately.
        RenderGraph g4 = new(new Extent2D(128, 128), "ValUsage");
        RGTexture colorOnly = g4.CreateTransientTexture(new RGTextureDesc
        {
            Name = "color_only",
            Format = Format.Rgba8Unorm,
            Usage = TextureUsage.ColorAttachment,
        });
        RGTexture autoTransient = g4.CreateTransientTexture(new RGTextureDesc
        {
            Name = "auto_transient",
            Format = Format.Rgba8Unorm,
        });
        bool supportsChecksOk = false;
        g4.Use(new LambdaNode(b =>
        {
            supportsChecksOk =
                !b.SupportsUsage(colorOnly, RGUsage.ComputeWrite) &&
                b.SupportsUsage(colorOnly, RGUsage.ColorAttachmentWrite) &&
                b.SupportsUsage(autoTransient, RGUsage.ComputeWrite) &&
                !b.SupportsUsage(b.Output, RGUsage.ComputeWrite) &&
                b.SupportsUsage(b.Output, RGUsage.ColorAttachmentWrite);

            RenderGraphPassBuilder p = b.AddPass("BadStorage", NullNode.Instance);
            p.Write(colorOnly, RGUsage.ComputeWrite);
            p.ProducesOutput();
        }));
        bool caughtBadUsage = false;
        try { g4.CompileDryRun(target); }
        catch (InvalidOperationException) { caughtBadUsage = true; }
        check(supportsChecksOk && caughtBadUsage, "SupportsUsage accurately reports texture capabilities and validation rejects incompatible explicit TextureUsage");
        g4.Destroy();

        // 5. Same-pass texture feedback conflict rejected (even when declared via separate Read + Write calls).
        RenderGraph g5 = new(new Extent2D(128, 128), "ValConflict");
        RGTexture rt = g5.CreateTransientTexture(new RGTextureDesc { Name = "rt", Format = Format.Rgba8Unorm });
        g5.Use(new LambdaNode(b =>
        {
            RenderGraphPassBuilder p0 = b.AddPass("Init", NullNode.Instance);
            p0.Write(rt, RGUsage.ColorAttachmentWrite);
            RenderGraphPassBuilder p1 = b.AddPass("Feedback", NullNode.Instance);
            p1.Read(rt, RGUsage.PixelRead);
            p1.Write(rt, RGUsage.ColorAttachmentWrite);
            p1.ProducesOutput();
        }));
        bool caughtConflict = false;
        try { g5.CompileDryRun(target); }
        catch (InvalidOperationException ex) { caughtConflict = ex.Message.Contains("Feedback") && ex.Message.Contains("rt"); }
        check(caughtConflict, "Validation rejects simultaneous ColorAttachmentWrite + PixelRead on same texture in one pass");
        g5.Destroy();

        // 6. ReadWrite without read flags rejected.
        RenderGraph g6 = new(new Extent2D(128, 128), "ValReadWriteMissingRead");
        RGBuffer b6 = g6.CreateTransientBuffer(new RGBufferDesc { Name = "rw_buf", SizeBytes = 256 });
        g6.Use(new LambdaNode(b =>
        {
            RenderGraphPassBuilder p0 = b.AddPass("Init");
            p0.Write(b6, RGUsage.ComputeWrite);
            RenderGraphPassBuilder p1 = b.AddPass("BadRW");
            p1.ReadWrite(b6, RGUsage.ComputeWrite);
            p1.ProducesOutput();
        }));
        bool caughtMissingRead = false;
        try { g6.CompileDryRun(target); }
        catch (ArgumentException) { caughtMissingRead = true; }
        check(caughtMissingRead, "Validation rejects ReadWrite declared without read usage flags");
        g6.Destroy();

        // 7. Read-only attachment + storage write conflict rejected, while DepthStencilRead + PixelRead is allowed.
        RenderGraph g7 = new(new Extent2D(128, 128), "ValAttachStorageConflict");
        RGTexture d7 = g7.CreateTransientTexture(new RGTextureDesc { Name = "depth", Format = Format.D32Float });
        bool allowReadOnlyDepthSample = true;
        g7.Use(new LambdaNode(b =>
        {
            RenderGraphPassBuilder p0 = b.AddPass("DepthPre");
            p0.Write(d7, RGUsage.DepthStencil);
            RenderGraphPassBuilder p1 = b.AddPass("DepthUse");
            p1.Read(d7, RGUsage.DepthStencilRead);
            if (allowReadOnlyDepthSample)
                p1.Read(d7, RGUsage.PixelRead);
            else
                p1.Write(d7, RGUsage.ComputeWrite);
            p1.ProducesOutput();
        }));
        check(g7.CompileDryRun(target) == 2, "Validation allows simultaneous DepthStencilRead + PixelRead in one pass");
        allowReadOnlyDepthSample = false;
        bool caughtAttachStorage = false;
        try { g7.CompileDryRun(target); }
        catch (InvalidOperationException) { caughtAttachStorage = true; }
        check(caughtAttachStorage, "Validation rejects simultaneous DepthStencilRead + ComputeWrite in one pass");
        g7.Destroy();

        // 8. Duplicate frame import of the same external object deduplicates so culling and barriers preserve write->read dependency.
        RenderGraph g8 = new(new Extent2D(128, 128), "DupImportGraph");
        object sharedToken = new();
        g8.Use(new LambdaNode(b =>
        {
            RGTexture h1 = b.ImportDryRunTexture(sharedToken, new Extent3D(128, 128, 1), Format.Rgba8Unorm,
                TextureUsage.ColorAttachment | TextureUsage.Sampled, "shared_w");
            RenderGraphPassBuilder p0 = b.AddPass("WriteShared");
            p0.Write(h1, RGUsage.ColorAttachmentWrite);

            RGTexture h2 = b.ImportDryRunTexture(sharedToken, new Extent3D(128, 128, 1), Format.Rgba8Unorm,
                TextureUsage.ColorAttachment | TextureUsage.Sampled, "shared_r");
            RenderGraphPassBuilder p1 = b.AddPass("ReadShared");
            p1.Read(h2, RGUsage.PixelRead);
            p1.Write(b.Output, RGUsage.ColorAttachmentWrite);
            p1.ProducesOutput();
        }));
        check(g8.CompileDryRun(target) == 2 && !IsPassCulled(g8, "WriteShared") &&
            g8.PassReports[1].Barrier == new RGBarrier
            {
                Before = Stage.ColorOutput,
                BeforeAccess = Access.ColorWrite,
                After = Stage.Pixel,
                AfterAccess = Access.ShaderRead,
            },
            "Duplicate frame imports of one external texture share a slot, preventing false culling and preserving RAW barriers");
        g8.Destroy();
    }

    private static void TestAliasingAndPooling(Action<bool, string> check)
    {
        RenderGraphTarget target = RenderGraphTarget.CreateDryRun(new Extent2D(256, 256), Format.Bgra8Srgb);
        RenderGraph graph = new(new Extent2D(256, 256), "AliasGraph") { IdleFrameRetireThreshold = 3 };
        RGTexture a = graph.CreateTransientTexture(new RGTextureDesc { Name = "A", Format = Format.Rgba16Float });
        RGTexture bTex = graph.CreateTransientTexture(new RGTextureDesc { Name = "B", Format = Format.Rgba16Float });
        RGTexture c = graph.CreateTransientTexture(new RGTextureDesc { Name = "C", Format = Format.Rgba16Float });
        RGTexture d = graph.CreateTransientTexture(new RGTextureDesc { Name = "D", Format = Format.Rgba16Float });
        RGBuffer buf1 = graph.CreateTransientBuffer(new RGBufferDesc { Name = "buf1", SizeBytes = 2048 });
        RGBuffer buf2 = graph.CreateTransientBuffer(new RGBufferDesc { Name = "buf2", SizeBytes = 3000 });
        bool useD = true;

        // Lifetimes:
        // A: [0, 1]  (written P0, read P1)
        // C: [0, 3]  (written P0, read P3) -> nested across B!
        // B: [1, 2]  (written P1 while A is read -> CANNOT alias A because LastPass(A)==FirstPass(B)==1!)
        // D: [2, 3]  (written P2 while B is read -> CAN alias A because LastPass(A)==1 < FirstPass(D)==2!)
        // buf1: [0, 1], buf2: [2, 3] -> CAN alias (both round to 4096 B bucket)!
        graph.Use(new LambdaNode(builder =>
        {
            RenderGraphPassBuilder p0 = builder.AddPass("P0", NullNode.Instance);
            p0.Write(a, RGUsage.ColorAttachmentWrite);
            p0.Write(c, RGUsage.ColorAttachmentWrite);
            p0.Write(buf1, RGUsage.ComputeWrite);

            RenderGraphPassBuilder p1 = builder.AddPass("P1", NullNode.Instance);
            p1.Read(a, RGUsage.PixelRead);
            p1.Read(buf1, RGUsage.ComputeRead);
            p1.Write(bTex, RGUsage.ColorAttachmentWrite);

            RenderGraphPassBuilder p2 = builder.AddPass("P2", NullNode.Instance);
            p2.Read(bTex, RGUsage.PixelRead);
            p2.Write(buf2, RGUsage.ComputeWrite);
            if (useD)
                p2.Write(d, RGUsage.ColorAttachmentWrite);

            RenderGraphPassBuilder p3 = builder.AddPass("P3", NullNode.Instance);
            p3.Read(c, RGUsage.PixelRead);
            p3.Read(buf2, RGUsage.ComputeRead);
            if (useD)
                p3.Read(d, RGUsage.PixelRead);
            p3.Write(builder.Output, RGUsage.ColorAttachmentWrite);
            p3.ProducesOutput();
        }));

        graph.CompileDryRun(target);
        int physA = graph.GetPhysicalIndex(a);
        int physB = graph.GetPhysicalIndex(bTex);
        int physC = graph.GetPhysicalIndex(c);
        int physD = graph.GetPhysicalIndex(d);
        int physBuf1 = graph.GetPhysicalIndex(buf1);
        int physBuf2 = graph.GetPhysicalIndex(buf2);

        check(physA != physB, "Boundary LastPass == FirstPass prevents aliasing A and B");
        check(physA != physC && physB != physC, "Nested lifetime C stays isolated from A and B");
        check(physD == physA, "Non-overlapping D (FirstPass=2 > LastPass(A)=1) aliases A");
        check(graph.Statistics.LogicalTransientTextureCount == 4 && graph.Statistics.ActivePhysicalTextureCount == 3,
            "4 logical transient textures materialize onto 3 physical pooled textures");
        check(physBuf1 == physBuf2 && graph.Statistics.ActivePhysicalBufferCount == 1,
            "Non-overlapping 2 KiB and 3 KiB transient buffers alias onto 1 physical 4 KiB buffer");
        check(graph.GetResolvedTextureUsage(a) == (TextureUsage.ColorAttachment | TextureUsage.Sampled),
            "Transient TextureUsage inferred minimally from pass accesses");

        // Frame 2: sticky assignments must stay stable without oscillation.
        graph.CompileDryRun(target);
        check(graph.GetPhysicalIndex(a) == physA && graph.GetPhysicalIndex(bTex) == physB &&
            graph.GetPhysicalIndex(c) == physC && graph.GetPhysicalIndex(d) == physD,
            "Steady-state sticky assignments keep physical indices deterministic across frames");

        // Idle eviction test: stop using a LOWER-INDEX texture so a HIGHER-INDEX active texture + frame transient get swap-moved!
        RenderGraph evictGraph = new(new Extent2D(64, 64), "EvictGraph") { IdleFrameRetireThreshold = 3 };
        RGTexture tempTex = evictGraph.CreateTransientTexture(new RGTextureDesc { Name = "temp_first", Format = Format.Rgba16Float });
        RGTexture keepTex = evictGraph.CreateTransientTexture(new RGTextureDesc { Name = "keep_second", Format = Format.Rgba8Unorm });
        bool useTemp = true;
        RGTexture frameTex = default;
        evictGraph.Use(new LambdaNode(b =>
        {
            frameTex = b.CreateTexture(new RGTextureDesc { Name = "frame_third", Format = Format.R32Float });
            RenderGraphPassBuilder p0 = b.AddPass("Work", NullNode.Instance);
            if (useTemp)
                p0.Write(tempTex, RGUsage.ColorAttachmentWrite);
            p0.Write(keepTex, RGUsage.ColorAttachmentWrite);
            p0.Write(frameTex, RGUsage.ColorAttachmentWrite);
            RenderGraphPassBuilder p1 = b.AddPass("Out", NullNode.Instance);
            if (useTemp)
                p1.Read(tempTex, RGUsage.PixelRead);
            p1.Read(keepTex, RGUsage.PixelRead);
            p1.Read(frameTex, RGUsage.PixelRead);
            p1.Write(b.Output, RGUsage.ColorAttachmentWrite);
            p1.ProducesOutput();
        }));
        evictGraph.CompileDryRun(target);
        check(evictGraph.Statistics.TotalPooledTextureCount == 3 && evictGraph.GetPhysicalIndex(tempTex) == 0 &&
            evictGraph.GetPhysicalIndex(keepTex) == 1 && evictGraph.GetPhysicalIndex(frameTex) == 2,
            "Pool assigns indices 0, 1, 2 before eviction");
        useTemp = false;
        for (int i = 0; i < 3; ++i)
            evictGraph.CompileDryRun(target);
        check(evictGraph.Statistics.TotalPooledTextureCount == 2 &&
            evictGraph.GetPhysicalIndex(keepTex) == 1 && evictGraph.GetPhysicalIndex(frameTex) == 0,
            "Idle eviction of slot 0 swap-moves active frame-scoped slot 2 into 0 and preserves live Physical indices");

        evictGraph.Destroy();
        graph.Destroy();
    }

    private static void TestBarrierPlanning(Action<bool, string> check)
    {
        RenderGraphTarget swapTarget = RenderGraphTarget.CreateDryRun(new Extent2D(128, 128), Format.Bgra8Srgb);

        // 1. RAW, RAR dedup, multi-stage RAR, global barrier propagation, and WAR on aliased physical slot.
        RenderGraph g = new(new Extent2D(128, 128), "BarrierGraph");
        RGTexture a = g.CreateTransientTexture(new RGTextureDesc { Name = "A", Format = Format.Rgba8Unorm });
        RGTexture bTex = g.CreateTransientTexture(new RGTextureDesc { Name = "B", Format = Format.Rgba16Float });
        RGTexture aliasA = g.CreateTransientTexture(new RGTextureDesc { Name = "AliasA", Format = Format.Rgba8Unorm });
        RGTexture computeOut = g.CreateTransientTexture(new RGTextureDesc { Name = "ComputeOut", Format = Format.Rgba8Unorm });

        g.Use(new LambdaNode(builder =>
        {
            RenderGraphPassBuilder p0 = builder.AddPass("WriteAB", NullNode.Instance);
            p0.Write(a, RGUsage.ColorAttachmentWrite);
            p0.Write(bTex, RGUsage.ColorAttachmentWrite);

            RenderGraphPassBuilder p1 = builder.AddPass("PixelReadA", NullNode.Instance);
            p1.Read(a, RGUsage.PixelRead);
            p1.ProducesOutput();

            // B was also written in ColorOutput/ColorWrite; P1's global barrier already synced ColorOutput->Pixel!
            RenderGraphPassBuilder p2 = builder.AddPass("PixelReadB_Propagated", NullNode.Instance);
            p2.Read(bTex, RGUsage.PixelRead);
            p2.ProducesOutput();

            // Different stage (ComputeRead) on B STILL requires ColorOutput[ColorWrite] -> Compute[ShaderRead]!
            RenderGraphPassBuilder p3 = builder.AddPass("ComputeReadB", NullNode.Instance);
            p3.Read(bTex, RGUsage.ComputeRead);
            p3.Write(computeOut, RGUsage.ComputeWrite);

            // AliasA starts at P4 > LastPass(A)=1, so it aliases A's physical slot (which was last read in Pixel at P1 -> WAR!).
            RenderGraphPassBuilder p4 = builder.AddPass("WriteAliasA", NullNode.Instance);
            p4.Write(aliasA, RGUsage.ColorAttachmentWrite);

            RenderGraphPassBuilder p5 = builder.AddPass("Final", NullNode.Instance);
            p5.Read(aliasA, RGUsage.PixelRead);
            p5.Read(computeOut, RGUsage.PixelRead);
            p5.Write(builder.Output, RGUsage.ColorAttachmentWrite);
            p5.ProducesOutput();
        }));

        g.CompileDryRun(swapTarget, outputFinalUsage: RGUsage.ColorAttachment);
        ReadOnlySpan<RenderGraphPassReport> f1 = g.PassReports;
        check(f1[0].Barrier.IsEmpty, "Frame 1 initial write needs no barrier");
        check(f1[1].Barrier == new RGBarrier
        {
            Before = Stage.ColorOutput,
            BeforeAccess = Access.ColorWrite,
            After = Stage.Pixel,
            AfterAccess = Access.ShaderRead,
        }, "RAW emits exact ColorOutput[ColorWrite] -> Pixel[ShaderRead]");
        check(f1[2].Barrier.IsEmpty, "Global barrier propagation eliminates redundant ColorOutput->Pixel barrier on sibling texture B");
        check(f1[3].Barrier == new RGBarrier
        {
            Before = Stage.ColorOutput,
            BeforeAccess = Access.ColorWrite,
            After = Stage.Compute,
            AfterAccess = Access.ShaderRead,
        }, "Second read in a different stage (Compute) emits exact RAW barrier");
        check(g.GetPhysicalIndex(aliasA) == g.GetPhysicalIndex(a) && f1[4].Barrier == new RGBarrier
        {
            Before = Stage.Pixel,
            BeforeAccess = Access.None,
            After = Stage.ColorOutput,
            AfterAccess = Access.ColorWrite,
        }, "Aliased overwrite emits WAR execution + write-domain visibility Pixel[None] -> ColorOutput[ColorWrite]");
        check(f1[5].Barrier == new RGBarrier
        {
            Before = Stage.ColorOutput | Stage.Compute,
            BeforeAccess = Access.ColorWrite | Access.ShaderWrite,
            After = Stage.Pixel,
            AfterAccess = Access.ShaderRead,
        }, "Multi-input pass merges barriers into a single exact barrier");
        check(g.ExitBarrier == new RGBarrier
        {
            Before = Stage.ColorOutput,
            BeforeAccess = Access.ColorWrite,
            After = Stage.ColorOutput,
            AfterAccess = Access.ColorRead | Access.ColorWrite,
        }, "outputFinalUsage=ColorAttachment emits exact trailing overlay barrier");

        // Frame 2: cross-frame WAR + write visibility emitted on pooled textures A/B/ComputeOut, while swapchain Output resets cleanly!
        g.CompileDryRun(swapTarget, outputFinalUsage: RGUsage.ColorAttachment);
        ReadOnlySpan<RenderGraphPassReport> f2 = g.PassReports;
        check(f2[0].Barrier == new RGBarrier
        {
            Before = Stage.Pixel | Stage.Compute,
            BeforeAccess = Access.None,
            After = Stage.ColorOutput,
            AfterAccess = Access.ColorWrite,
        }, "Frame 2 automatically emits cross-frame WAR + write visibility barrier on persistent physical pooled textures");
        g.Destroy();

        // 2. Imported buffer with HostRead exit barrier + offscreen imported texture with PixelRead exit barrier & Frame 2 WAR.
        RenderGraph ext = new(new Extent2D(64, 64), "ExternalSyncGraph");
        object readbackToken = new();
        object offscreenToken = new();
        RGBuffer readbackBuf = ext.ImportDryRunBuffer(readbackToken, 1024, "readback_buf", RGUsage.HostRead);
        RGTexture offscreenTex = ext.ImportDryRunTexture(offscreenToken, new Extent3D(64, 64, 1), Format.Rgba8Unorm,
            TextureUsage.ColorAttachment | TextureUsage.Sampled, "offscreen_tex", RGUsage.PixelRead);
        ext.Use(new LambdaNode(b =>
        {
            RenderGraphPassBuilder p0 = b.AddPass("RenderOffscreen", NullNode.Instance);
            p0.Write(offscreenTex, RGUsage.ColorAttachmentWrite);

            RenderGraphPassBuilder p1 = b.AddPass("CopyReadback", NullNode.Instance);
            p1.Write(readbackBuf, RGUsage.TransferWrite);
        }));

        ext.CompileDryRun();
        check(ext.Statistics.AlivePassCount == 2, "Imported resources with FinalUsage keep producer passes alive without Output");
        check(ext.ExitBarrier == new RGBarrier
        {
            Before = Stage.ColorOutput | Stage.Transfer,
            BeforeAccess = Access.ColorWrite | Access.TransferWrite,
            After = Stage.Pixel | Stage.Host,
            AfterAccess = Access.ShaderRead | Access.HostRead,
        }, "Exit barrier merges PixelRead and HostRead external handoffs accurately");

        // Frame 2: offscreenTex gets Pixel->ColorOutput WAR + ColorWrite visibility, while Host pseudo-stage does NOT leak into Transfer WAR!
        ext.CompileDryRun();
        check(ext.PassReports[0].Barrier == new RGBarrier
        {
            Before = Stage.Pixel,
            BeforeAccess = Access.None,
            After = Stage.ColorOutput,
            AfterAccess = Access.ColorWrite,
        }, "Frame 2 emits WAR + write visibility against prior frame's external FinalUsage=PixelRead");
        check(ext.PassReports[1].Barrier == new RGBarrier
        {
            Before = Stage.Transfer,
            BeforeAccess = Access.TransferWrite,
            After = Stage.Transfer,
            AfterAccess = Access.TransferWrite,
        }, "Frame 2 readback buffer emits clean WAW without invalid Stage.Host source stage");
        ext.Destroy();

        // 3. Write -> Read -> ReadWrite preserves write-domain visibility even when Read already synced SyncedReads;
        //    pure WAR on read-only imported resource stays execution-only (Access.None -> Access.None).
        RenderGraph rwGraph = new(new Extent2D(64, 64), "WriteReadReadWriteGraph");
        RGBuffer rwBuf = rwGraph.CreateTransientBuffer(new RGBufferDesc { Name = "rw_buf", SizeBytes = 512 });
        object readonlyToken = new();
        RGBuffer roBuf = rwGraph.ImportDryRunBuffer(readonlyToken, 512, "ro_buf");
        rwGraph.Use(new LambdaNode(b =>
        {
            RenderGraphPassBuilder p0 = b.AddPass("InitBuf");
            p0.Write(rwBuf, RGUsage.ComputeWrite);
            p0.Read(roBuf, RGUsage.ComputeRead);

            RenderGraphPassBuilder p1 = b.AddPass("ReadBuf");
            p1.Read(rwBuf, RGUsage.ComputeRead);
            p1.ProducesOutput();

            RenderGraphPassBuilder p2 = b.AddPass("ReadWriteBuf");
            p2.ReadWrite(rwBuf, RGUsage.ComputeReadWrite);
            p2.ProducesOutput();

            RenderGraphPassBuilder p3 = b.AddPass("PureWarWrite");
            p3.Write(roBuf, RGUsage.ComputeWrite);
            p3.ProducesOutput();
        }));
        rwGraph.CompileDryRun();
        check(rwGraph.PassReports[2].Barrier == new RGBarrier
        {
            Before = Stage.Compute,
            BeforeAccess = Access.None,
            After = Stage.Compute,
            AfterAccess = Access.ShaderWrite,
        }, "Write -> Read -> ReadWrite emits ShaderWrite visibility even when ComputeRead was already synced");
        check(rwGraph.PassReports[3].Barrier == new RGBarrier
        {
            Before = Stage.Compute,
            BeforeAccess = Access.None,
            After = Stage.Compute,
            AfterAccess = Access.None,
        }, "Pure WAR on previously unwritten imported buffer stays execution-only");
        rwGraph.Destroy();

        // 4. External sync entry eviction swap-remove must preserve live Physical.Index for higher-index active externals!
        RenderGraph extEvict = new(new Extent2D(64, 64), "ExternalEvictGraph");
        object extToken0 = new();
        object extToken1 = new();
        RGBuffer ext0 = extEvict.ImportDryRunBuffer(extToken0, 256, "ext0", RGUsage.ComputeRead);
        RGTexture ext1 = extEvict.ImportDryRunTexture(extToken1, new Extent3D(64, 64, 1), Format.Rgba8Unorm,
            TextureUsage.ColorAttachment | TextureUsage.Sampled, "ext1", RGUsage.PixelRead);
        bool useExt0 = true;
        extEvict.Use(new LambdaNode(b =>
        {
            if (useExt0)
            {
                RenderGraphPassBuilder p0 = b.AddPass("WriteExt0");
                p0.Write(ext0, RGUsage.ComputeWrite);
            }
            RenderGraphPassBuilder p1 = b.AddPass("WriteExt1");
            p1.Write(ext1, RGUsage.ColorAttachmentWrite);
        }));
        extEvict.CompileDryRun(); // Frame 1: ext0 at external[0], ext1 at external[1]
        useExt0 = false;
        extEvict.CompileDryRun(); // Frame 2: ext0 idle (LastUsedFrame=1, frameNumber=2 -> not yet > 2)
        extEvict.CompileDryRun(); // Frame 3: ext0 evicted -> ext1 swap-moved from external[1] to external[0]!
        check(extEvict.GetPhysicalIndex(ext1) == 0 &&
            extEvict.PassReports[0].Barrier == new RGBarrier
            {
                Before = Stage.Pixel,
                BeforeAccess = Access.None,
                After = Stage.ColorOutput,
                AfterAccess = Access.ColorWrite,
            } &&
            extEvict.ExitBarrier == new RGBarrier
            {
                Before = Stage.ColorOutput,
                BeforeAccess = Access.ColorWrite,
                After = Stage.Pixel,
                AfterAccess = Access.ShaderRead,
            },
            "Evicting lower-index external sync entry updates live Physical.Index and preserves barriers for swap-moved external");
        extEvict.Destroy();

        // 5. PropagateGlobalBarrier with Stage.AllCommands must still require beforeAccess to cover WriteAccess.
        RenderGraphPools testPools = new(null);
        int extIdx = testPools.GetOrAddExternal(new object(), 1);
        ref PhysicalSyncState extSync = ref testPools.GetSyncState(new PhysicalRef(PhysicalKind.External, extIdx));
        extSync.HasWrite = true;
        extSync.WriteStage = Stage.ColorOutput;
        extSync.WriteAccess = Access.ColorWrite;
        testPools.PropagateGlobalBarrier(Stage.AllCommands, Access.TransferWrite, Stage.Pixel, Access.ShaderRead);
        check(extSync.SyncedReads == RGUsage.None,
            "PropagateGlobalBarrier with Stage.AllCommands does not mark ColorWrite synced when beforeAccess lacks ColorWrite");
        testPools.PropagateGlobalBarrier(Stage.AllCommands, Access.ColorWrite, Stage.Pixel, Access.ShaderRead);
        check(extSync.SyncedReads == RGUsage.PixelRead,
            "PropagateGlobalBarrier with Stage.AllCommands marks ColorWrite synced once beforeAccess covers ColorWrite");
        testPools.Destroy();
    }

    private static void TestZeroSteadyStateAllocations(Action<bool, string> check)
    {
        RenderGraph graph = new(new Extent2D(1280, 720), "ZeroAllocGraph");
        RGTexture albedo = graph.CreateTransientTexture(new RGTextureDesc { Name = "albedo", Format = Format.Rgba8Unorm });
        RGTexture depth = graph.CreateTransientTexture(new RGTextureDesc { Name = "depth", Format = Format.D32Float });
        RGTexture hdr = graph.CreateTransientTexture(new RGTextureDesc { Name = "hdr", Format = Format.Rgba16Float });
        RenderChain chain = new();
        RGTextureDesc halfDesc = new() { Name = "bloom_half", Scale = 0.5f, Format = Format.Rgba16Float };
        RGTextureDesc ldrDesc = new() { Name = "ldr", Format = Format.Rgba8Unorm };

        graph.Use(new LambdaNode(b =>
        {
            RenderGraphPassBuilder gbuf = b.AddPass("GBuf", NullNode.Instance);
            gbuf.Write(albedo, RGUsage.ColorAttachmentWrite);
            gbuf.Write(depth, RGUsage.DepthStencil);

            RenderGraphPassBuilder light = b.AddPass("Light", NullNode.Instance);
            light.Read(albedo, RGUsage.PixelRead);
            light.Read(depth, RGUsage.PixelRead);
            light.Write(hdr, RGUsage.ColorAttachmentWrite);
            chain.Reset(hdr);

            RGTexture bloom = b.CreateTexture(in halfDesc);
            RenderGraphPassBuilder bloomPass = b.AddPass("Bloom", NullNode.Instance);
            bloomPass.Read(chain.Current, RGUsage.ComputeRead);
            bloomPass.Write(bloom, RGUsage.ComputeWrite);

            RGTexture ldr = b.CreateTexture(in ldrDesc);
            RenderGraphPassBuilder tone = b.AddPass("Tonemap", NullNode.Instance);
            tone.Read(chain.Current, RGUsage.PixelRead);
            tone.Read(bloom, RGUsage.PixelRead);
            tone.Write(ldr, RGUsage.ColorAttachmentWrite);
            chain.Advance(ldr);

            RenderGraphPassBuilder blit = b.AddPass("Blit", NullNode.Instance);
            blit.Read(chain.Current, RGUsage.PixelRead);
            blit.Write(b.Output, RGUsage.ColorAttachmentWrite);
            blit.ProducesOutput();
        }));

        RenderGraphTarget target = RenderGraphTarget.CreateDryRun(new Extent2D(1280, 720), Format.Bgra8Srgb);
        for (int i = 0; i < 5; ++i)
            graph.CompileDryRun(target, RGUsage.ColorAttachment);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; ++i)
            graph.CompileDryRun(target, RGUsage.ColorAttachment);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        check(allocated == 0, $"Steady-state RenderGraph compilation allocates 0 bytes over 100 frames (allocated={allocated})");
        graph.Destroy();
    }

    private sealed class NullNode : IRenderGraphNode
    {
        internal static readonly NullNode Instance = new();
        public void Setup(RenderGraphBuilder builder) { }
        public void Execute(in RenderGraphContext context, int passId) { }
    }

    private sealed class LambdaNode : IRenderGraphNode
    {
        private readonly Action<RenderGraphBuilder> _setup;
        internal LambdaNode(Action<RenderGraphBuilder> setup) => _setup = setup;
        public void Setup(RenderGraphBuilder builder) => _setup(builder);
        public void Execute(in RenderGraphContext context, int passId) { }
    }

    private sealed class NodeA : IRenderGraphNode { public void Setup(RenderGraphBuilder b) { } public void Execute(in RenderGraphContext c, int p) { } }
    private sealed class NodeB : IRenderGraphNode { public void Setup(RenderGraphBuilder b) { } public void Execute(in RenderGraphContext c, int p) { } }
    private sealed class NodeC : IRenderGraphNode { public void Setup(RenderGraphBuilder b) { } public void Execute(in RenderGraphContext c, int p) { } }
    private sealed class NodeD : IRenderGraphNode { public void Setup(RenderGraphBuilder b) { } public void Execute(in RenderGraphContext c, int p) { } }
}
