namespace Alco.Rendering;

/// <summary>
/// The per-frame scheduler of a <see cref="RenderGraph"/>: validates the declared
/// dependencies, culls nodes whose output is unused, and computes transient resource
/// lifetimes. This type is pure scheduling logic — it touches no GPU objects and is
/// unit-tested through fake nodes/resources.
/// <br/>All working sets are reused across frames; a steady-state compile allocates
/// no managed memory.
/// </summary>
internal sealed class RenderGraphCompiler
{
    private bool[] _alive = new bool[16];
    private int[] _firstTouch = new int[16];
    private int[] _lastTouch = new int[16];
    private readonly HashSet<RenderGraphTexture> _needed = new();
    private readonly HashSet<RenderGraphTexture> _written = new();

    /// <summary>
    /// For each node (by registration index), whether it survived culling this frame.
    /// </summary>
    internal ReadOnlySpan<bool> Alive => _alive;

    /// <summary>
    /// For each resource (by <see cref="RenderGraphTexture.Id"/>), the registration
    /// index of the first alive node writing it this frame, or -1.
    /// </summary>
    internal ReadOnlySpan<int> FirstTouch => _firstTouch;

    /// <summary>
    /// For each resource (by <see cref="RenderGraphTexture.Id"/>), the registration
    /// index of the last alive node reading or writing it this frame, or -1.
    /// </summary>
    internal ReadOnlySpan<int> LastTouch => _lastTouch;

    /// <summary>
    /// Runs the schedule computation for the current frame's declarations.
    /// </summary>
    /// <param name="records">The node records in registration order, with this frame's
    /// Setup declarations already captured. Execution order is registration order.</param>
    /// <param name="resources">All resources registered on the graph.</param>
    /// <exception cref="InvalidOperationException">
    /// A transient resource is read before any enabled node writes it, or a shared
    /// depth source is not written before its dependent resource.
    /// </exception>
    internal void Compile(List<RenderGraphNodeRecord> records, List<RenderGraphTexture> resources)
    {
        EnsureCapacity(records.Count, resources.Count);
        Array.Fill(_firstTouch, -1, 0, resources.Count);
        Array.Fill(_lastTouch, -1, 0, resources.Count);

        Validate(records);

        // Culling: backward reachability from output roots. `needed` accumulates the
        // resources consumed by alive later nodes; a node is alive when it is a root
        // or writes something needed. ReadWrite nodes read and write the same
        // resource, so removing writes before adding reads propagates the dependency
        // to the earlier writer.
        _needed.Clear();
        for (int i = records.Count - 1; i >= 0; i--)
        {
            RenderGraphNodeRecord record = records[i];
            if (!record.EnabledThisFrame)
            {
                _alive[i] = false;
                continue;
            }

            bool alive = record.ProducesOutput;
            if (!alive)
            {
                ReadOnlySpan<RenderGraphTexture> writes = record.WritesSpan;
                for (int w = 0; w < writes.Length; w++)
                {
                    if (_needed.Contains(writes[w]))
                    {
                        alive = true;
                        break;
                    }
                }
            }
            _alive[i] = alive;

            if (alive)
            {
                ReadOnlySpan<RenderGraphTexture> writes = record.WritesSpan;
                for (int w = 0; w < writes.Length; w++)
                {
                    _needed.Remove(writes[w]);
                }
                AddDependencies(record, _needed);
            }
        }

        // Lifetimes: forward scan over alive nodes. A transient is acquired at its
        // first writer and released after its last consumer. Depth sources count as
        // implicit reads of every writer of the dependent resource, so the shared
        // depth stays alive for the whole usage span.
        for (int i = 0; i < records.Count; i++)
        {
            if (!_alive[i])
            {
                continue;
            }

            RenderGraphNodeRecord record = records[i];
            ReadOnlySpan<RenderGraphTexture> writes = record.WritesSpan;
            for (int w = 0; w < writes.Length; w++)
            {
                RenderGraphTexture texture = writes[w];
                if (texture.Kind != RenderGraphTexture.ResourceKind.Transient)
                {
                    continue;
                }
                if (_firstTouch[texture.Id] < 0)
                {
                    _firstTouch[texture.Id] = i;
                }
                _lastTouch[texture.Id] = i;
            }

            ReadOnlySpan<RenderGraphTexture> reads = record.ReadsSpan;
            for (int r = 0; r < reads.Length; r++)
            {
                RenderGraphTexture texture = reads[r];
                if (texture.Kind == RenderGraphTexture.ResourceKind.Transient)
                {
                    _lastTouch[texture.Id] = i;
                }
            }

            for (int w = 0; w < writes.Length; w++)
            {
                RenderGraphTexture? depthSource = writes[w].DepthSource;
                if (depthSource != null && depthSource.Kind == RenderGraphTexture.ResourceKind.Transient)
                {
                    _lastTouch[depthSource.Id] = i;
                }
            }
        }
    }

    /// <summary>
    /// Validates read-before-write and shared-depth-source availability over the
    /// enabled nodes in execution (registration) order.
    /// </summary>
    private void Validate(List<RenderGraphNodeRecord> records)
    {
        _written.Clear();
        for (int i = 0; i < records.Count; i++)
        {
            RenderGraphNodeRecord record = records[i];
            if (!record.EnabledThisFrame)
            {
                continue;
            }

            ReadOnlySpan<RenderGraphTexture> reads = record.ReadsSpan;
            for (int r = 0; r < reads.Length; r++)
            {
                RenderGraphTexture texture = reads[r];
                if (texture.Kind == RenderGraphTexture.ResourceKind.Transient
                    && !_written.Contains(texture)
                    && !record.WritesContains(texture))
                {
                    throw new InvalidOperationException(
                        $"Render graph node '{record.Node.GetType().Name}' reads transient resource '{texture.Name}' " +
                        "before any enabled earlier node writes it this frame.");
                }
            }

            ReadOnlySpan<RenderGraphTexture> writes = record.WritesSpan;
            for (int w = 0; w < writes.Length; w++)
            {
                RenderGraphTexture texture = writes[w];
                RenderGraphTexture? depthSource = texture.DepthSource;
                if (depthSource != null
                    && depthSource.Kind == RenderGraphTexture.ResourceKind.Transient
                    && !_written.Contains(depthSource)
                    && !record.WritesContains(depthSource))
                {
                    throw new InvalidOperationException(
                        $"Render graph node '{record.Node.GetType().Name}' writes '{texture.Name}', which shares the " +
                        $"depth of '{depthSource.Name}', but no enabled earlier node writes that depth source this frame.");
                }
                _written.Add(texture);
            }
        }
    }

    /// <summary>
    /// Adds a node's full dependency set (declared reads plus the transient depth
    /// sources of its writes) to the set.
    /// </summary>
    private static void AddDependencies(RenderGraphNodeRecord record, HashSet<RenderGraphTexture> target)
    {
        ReadOnlySpan<RenderGraphTexture> reads = record.ReadsSpan;
        for (int r = 0; r < reads.Length; r++)
        {
            target.Add(reads[r]);
        }
        ReadOnlySpan<RenderGraphTexture> writes = record.WritesSpan;
        for (int w = 0; w < writes.Length; w++)
        {
            RenderGraphTexture? depthSource = writes[w].DepthSource;
            if (depthSource != null && depthSource.Kind == RenderGraphTexture.ResourceKind.Transient)
            {
                target.Add(depthSource);
            }
        }
    }

    private void EnsureCapacity(int nodeCount, int resourceCount)
    {
        if (_alive.Length < nodeCount)
        {
            Array.Resize(ref _alive, Math.Max(nodeCount, _alive.Length * 2));
        }
        if (_firstTouch.Length < resourceCount)
        {
            int capacity = Math.Max(resourceCount, _firstTouch.Length * 2);
            Array.Resize(ref _firstTouch, capacity);
            Array.Resize(ref _lastTouch, capacity);
        }
    }
}
