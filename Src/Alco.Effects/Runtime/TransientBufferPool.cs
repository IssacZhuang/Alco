using System.Numerics;
using Alco.Graphics;

namespace Alco.Effects;

/// <summary>
/// Pools reusable <see cref="Alco.Graphics.Buffer"/> instances for per-frame transient GPU uploads
/// and work tables, never overwriting a buffer until its last recorded frame has completed.
/// </summary>
internal sealed class TransientBufferPool : AutoDisposable
{
    private struct Entry
    {
        public Alco.Graphics.Buffer Buffer;
        public ulong LastFrameId;
    }

    private readonly RenderingSystem _rendering;
    private readonly string _name;
    private readonly List<Entry> _entries = [];

    /// <summary>Creates a transient buffer pool backed by <paramref name="rendering"/>.</summary>
    /// <param name="rendering">The rendering system owning created GPU buffers.</param>
    /// <param name="name">The diagnostic label prefix.</param>
    public TransientBufferPool(RenderingSystem rendering, string name)
    {
        ArgumentNullException.ThrowIfNull(rendering);
        _rendering = rendering;
        _name = name;
    }

    /// <summary>Gets the number of pooled buffers currently held by this pool.</summary>
    public int PooledCount => _entries.Count;

    /// <summary>
    /// Acquires a buffer of at least <paramref name="minSizeBytes"/> that is safe to overwrite
    /// during <paramref name="context"/>'s active frame.
    /// </summary>
    /// <param name="context">The active render context recording commands.</param>
    /// <param name="minSizeBytes">Minimum required byte size.</param>
    /// <returns>A buffer whose <see cref="Alco.Graphics.Buffer.SizeBytes"/> is at least <paramref name="minSizeBytes"/>.</returns>
    public Alco.Graphics.Buffer Acquire(RenderContext context, ulong minSizeBytes)
    {
        ArgumentNullException.ThrowIfNull(context);
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        int bestIndex = -1;
        ulong bestSize = ulong.MaxValue;
        ulong currentFrame = context.CurrentFrameId;

        for (int i = 0; i < _entries.Count; i++)
        {
            Entry entry = _entries[i];
            if (entry.Buffer.SizeBytes < minSizeBytes)
                continue;
            if (!context.IsFrameCompleted(entry.LastFrameId))
                continue;
            if (entry.Buffer.SizeBytes < bestSize)
            {
                bestSize = entry.Buffer.SizeBytes;
                bestIndex = i;
            }
        }

        if (bestIndex >= 0)
        {
            Entry reused = _entries[bestIndex];
            reused.LastFrameId = currentFrame;
            _entries[bestIndex] = reused;
            return reused.Buffer;
        }

        // Prune idle completed buffers above a generous cap to prevent unbounded retention
        // when requested allocation sizes grow geometrically.
        for (int i = _entries.Count - 1; i >= 0 && _entries.Count >= 16; i--)
        {
            if (context.IsFrameCompleted(_entries[i].LastFrameId))
            {
                _entries[i].Buffer.Destroy();
                _entries.RemoveAt(i);
            }
        }

        ulong clamped = Math.Max(minSizeBytes, 256UL);
        ulong allocSize = clamped <= uint.MaxValue
            ? BitOperations.RoundUpToPowerOf2((uint)clamped)
            : clamped;
        Alco.Graphics.Buffer created = _rendering.CreateBuffer(allocSize);
        _entries.Add(new Entry { Buffer = created, LastFrameId = currentFrame });
        return created;
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (!disposing)
            return;

        for (int i = 0; i < _entries.Count; i++)
            _entries[i].Buffer.Destroy();
        _entries.Clear();
    }
}
