namespace Alco.Rendering;

/// <summary>
/// The graph-side registration record of one <see cref="IRenderGraphNode"/>: the node
/// itself plus the per-frame dependency declaration captured during
/// <see cref="IRenderGraphNode.Setup"/>. The read/write arrays are allocated once at
/// registration and only ever grow, so the per-frame <see cref="ResetPerFrame"/> is
/// allocation-free.
/// </summary>
internal sealed class RenderGraphNodeRecord
{
    private RenderGraphTexture[] _reads = new RenderGraphTexture[4];
    private RenderGraphTexture[] _writes = new RenderGraphTexture[4];

    /// <summary>The registered node.</summary>
    internal readonly IRenderGraphNode Node;

    /// <summary>The number of valid entries in <see cref="ReadsSpan"/> this frame.</summary>
    internal int ReadCount { get; private set; }

    /// <summary>The number of valid entries in <see cref="WritesSpan"/> this frame.</summary>
    internal int WriteCount { get; private set; }

    /// <summary>Whether the node declared <see cref="RenderGraphBuilder.ProducesOutput"/> this frame.</summary>
    internal bool ProducesOutput { get; private set; }

    /// <summary>Whether the node is enabled this frame (snapshot of <see cref="IRenderNode.IsEnabled"/> at setup time).</summary>
    internal bool EnabledThisFrame { get; private set; }

    internal RenderGraphNodeRecord(IRenderGraphNode node)
    {
        Node = node;
    }

    /// <summary>The declared reads of this frame.</summary>
    internal ReadOnlySpan<RenderGraphTexture> ReadsSpan => _reads.AsSpan(0, ReadCount);

    /// <summary>The declared writes of this frame.</summary>
    internal ReadOnlySpan<RenderGraphTexture> WritesSpan => _writes.AsSpan(0, WriteCount);

    /// <summary>Clears the per-frame declaration, keeping the arrays for reuse.</summary>
    internal void ResetPerFrame()
    {
        ReadCount = 0;
        WriteCount = 0;
        ProducesOutput = false;
        EnabledThisFrame = Node.IsEnabled;
    }

    /// <summary>Appends a read declaration; duplicates (including a previous write) are folded.</summary>
    internal void AddRead(RenderGraphTexture texture)
    {
        if (Contains(_reads, ReadCount, texture))
        {
            return;
        }
        if (ReadCount == _reads.Length)
        {
            Array.Resize(ref _reads, _reads.Length * 2);
        }
        _reads[ReadCount++] = texture;
    }

    /// <summary>Appends a write declaration; duplicates are folded.</summary>
    internal void AddWrite(RenderGraphTexture texture)
    {
        if (Contains(_writes, WriteCount, texture))
        {
            return;
        }
        if (WriteCount == _writes.Length)
        {
            Array.Resize(ref _writes, _writes.Length * 2);
        }
        _writes[WriteCount++] = texture;
    }

    /// <summary>Whether the node declared a write of <paramref name="texture"/> this frame.</summary>
    internal bool WritesContains(RenderGraphTexture texture)
    {
        return Contains(_writes, WriteCount, texture);
    }

    /// <summary>Marks the node as a graph output root for this frame.</summary>
    internal void MarkProducesOutput()
    {
        ProducesOutput = true;
    }

    private static bool Contains(RenderGraphTexture[] array, int count, RenderGraphTexture texture)
    {
        for (int i = 0; i < count; i++)
        {
            if (ReferenceEquals(array[i], texture))
            {
                return true;
            }
        }
        return false;
    }
}
