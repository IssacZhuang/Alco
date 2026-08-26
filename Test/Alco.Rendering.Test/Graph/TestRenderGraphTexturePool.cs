using NUnit.Framework;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.Rendering.Test;

/// <summary>
/// Pure-logic tests of the internal <see cref="RenderGraphTexturePool"/> with plain
/// object handles: factory misses, the allocation priority order (sticky from Freed,
/// most-recently-freed LIFO, sticky from Idle, oldest idle), frame resets, clearing
/// and key isolation.
/// </summary>
[TestFixture]
public sealed class TestRenderGraphTexturePool
{
    private sealed class FakeHandle : IDisposable
    {
        public readonly string Name;
        public bool Disposed;

        public FakeHandle(string name)
        {
            Name = name;
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }

    private static readonly TexturePoolKey KeyA = new(16, 16, PixelFormat.RGBA8Unorm, GPUFrameBuffer.ColorAttachmentUsage);
    private static readonly TexturePoolKey KeyB = new(32, 32, PixelFormat.RGBA8Unorm, GPUFrameBuffer.ColorAttachmentUsage);

    private static RenderGraphTexturePool CreatePool(out List<TexturePoolKey> factoryKeys, out List<string> factoryNames)
    {
        var keys = new List<TexturePoolKey>();
        var names = new List<string>();
        var pool = new RenderGraphTexturePool((key, name) =>
        {
            keys.Add(key);
            names.Add(name);
            return new FakeHandle(name);
        });
        factoryKeys = keys;
        factoryNames = names;
        return pool;
    }

    [Test(Description = "An allocate miss on an empty pool invokes the factory with the requested key and name")]
    public void AllocateMissInvokesFactory()
    {
        using RenderGraphTexturePool pool = CreatePool(out List<TexturePoolKey> keys, out List<string> names);

        object handle = pool.Allocate(KeyA, null, "first");

        Assert.That(handle, Is.TypeOf<FakeHandle>());
        Assert.That(((FakeHandle)handle).Name, Is.EqualTo("first"));
        Assert.That(keys, Is.EqualTo(new[] { KeyA }));
        Assert.That(names, Is.EqualTo(new[] { "first" }));
        Assert.That(pool.TotalCount, Is.EqualTo(1));
        Assert.That(pool.TotalCountFor(KeyA), Is.EqualTo(1));
        Assert.That(pool.IdleCountFor(KeyA), Is.EqualTo(0), "The allocated entry is occupied, not idle.");
    }

    [Test(Description = "A freed entry is preferred over idle entries: the most recently released entry is reused first")]
    public void FreedEntryIsPreferredOverIdle()
    {
        using RenderGraphTexturePool pool = CreatePool(out _, out _);
        object x = pool.Allocate(KeyA, null, "x");
        object y = pool.Allocate(KeyA, null, "y");
        pool.BeginFrame();

        object occupied = pool.Allocate(KeyA, null, "ignored");
        Assert.That(ReferenceEquals(occupied, x), Is.True, "The oldest idle entry is taken first.");
        pool.ReleaseExpired(KeyA, occupied);

        object reused = pool.Allocate(KeyA, null, "ignored");

        Assert.That(ReferenceEquals(reused, x), Is.True, "The entry released earlier in this walk must be reused before idle entries.");
        Assert.That(ReferenceEquals(reused, y), Is.False);
        Assert.That(pool.IdleCountFor(KeyA), Is.EqualTo(1), "The other idle entry stays untouched.");
    }

    [Test(Description = "A sticky entry is taken back out of the freed set even when it is not the most recently released")]
    public void StickyEntryIsRecoveredFromFreed()
    {
        using RenderGraphTexturePool pool = CreatePool(out _, out _);
        object x = pool.Allocate(KeyA, null, "x");
        object z = pool.Allocate(KeyA, null, "z");
        pool.BeginFrame();
        pool.Allocate(KeyA, null, "ignored"); // takes X (oldest idle)
        pool.Allocate(KeyA, null, "ignored"); // takes Z
        pool.ReleaseExpired(KeyA, x);
        pool.ReleaseExpired(KeyA, z); // Freed = [X, Z]; LIFO alone would hand out Z

        object sticky = pool.Allocate(KeyA, x, "ignored");

        Assert.That(ReferenceEquals(sticky, x), Is.True, "The sticky entry wins over the most recently released one.");

        object lifo = pool.Allocate(KeyA, null, "ignored");
        Assert.That(ReferenceEquals(lifo, z), Is.True, "The remaining freed entry is still available.");
    }

    [Test(Description = "A sticky entry still idle at allocation time is reassigned and removed from the idle set")]
    public void StickyEntryIsRecoveredFromIdle()
    {
        using RenderGraphTexturePool pool = CreatePool(out _, out _);
        object x = pool.Allocate(KeyA, null, "x");
        object y = pool.Allocate(KeyA, null, "y");
        pool.BeginFrame(); // Idle = [X, Y]

        object sticky = pool.Allocate(KeyA, x, "ignored");

        Assert.That(ReferenceEquals(sticky, x), Is.True);
        Assert.That(pool.IdleCountFor(KeyA), Is.EqualTo(1), "The sticky entry was removed from the idle set.");

        object fallback = pool.Allocate(KeyA, null, "ignored");
        Assert.That(ReferenceEquals(fallback, y), Is.True, "Only the other idle entry remains.");
    }

    [Test(Description = "With no freed entries and no sticky, the oldest idle entry is the deterministic fallback")]
    public void OldestIdleEntryIsTheFallback()
    {
        using RenderGraphTexturePool pool = CreatePool(out _, out _);
        object y = pool.Allocate(KeyA, null, "y"); // materialized first -> oldest
        object z = pool.Allocate(KeyA, null, "z");
        pool.BeginFrame(); // Idle = [Y, Z] in materialization order

        object first = pool.Allocate(KeyA, null, "ignored");
        object second = pool.Allocate(KeyA, null, "ignored");

        Assert.That(ReferenceEquals(first, y), Is.True);
        Assert.That(ReferenceEquals(second, z), Is.True);
        Assert.That(pool.IdleCountFor(KeyA), Is.EqualTo(0));
    }

    [Test(Description = "BeginFrame returns every occupied entry to the idle set and clears the freed set")]
    public void BeginFrameResetsTheWalk()
    {
        using RenderGraphTexturePool pool = CreatePool(out _, out _);
        pool.Allocate(KeyA, null, "x");
        pool.Allocate(KeyA, null, "y");
        pool.BeginFrame();
        Assert.That(pool.IdleCountFor(KeyA), Is.EqualTo(2));

        pool.Allocate(KeyA, null, "ignored");
        pool.Allocate(KeyA, null, "ignored");
        Assert.That(pool.IdleCountFor(KeyA), Is.EqualTo(0), "Both entries are occupied after the walk.");

        pool.BeginFrame();
        Assert.That(pool.IdleCountFor(KeyA), Is.EqualTo(2), "Every materialized entry is idle again after BeginFrame.");
        Assert.That(pool.TotalCountFor(KeyA), Is.EqualTo(2));
    }

    [Test(Description = "Clear disposes every materialized entry and empties all pool state")]
    public void ClearDisposesEverything()
    {
        using RenderGraphTexturePool pool = CreatePool(out _, out _);
        var first = (FakeHandle)pool.Allocate(KeyA, null, "first");
        var second = (FakeHandle)pool.Allocate(KeyA, null, "second");
        var otherKey = (FakeHandle)pool.Allocate(KeyB, null, "other");
        pool.BeginFrame();

        pool.Clear();

        Assert.That(first.Disposed, Is.True);
        Assert.That(second.Disposed, Is.True);
        Assert.That(otherKey.Disposed, Is.True);
        Assert.That(pool.TotalCount, Is.EqualTo(0));
        Assert.That(pool.TotalCountFor(KeyA), Is.EqualTo(0));
        Assert.That(pool.IdleCountFor(KeyA), Is.EqualTo(0));

        object rerented = pool.Allocate(KeyA, null, "after_clear");
        Assert.That(ReferenceEquals(rerented, first), Is.False, "A cleared pool materializes fresh entries.");
        Assert.That(pool.TotalCount, Is.EqualTo(1));
    }

    [Test(Description = "PruneExcept disposes entries outside the keep set, rebuilds the walk state and drops empty key states")]
    public void PruneExceptDisposesUnkeptEntries()
    {
        using RenderGraphTexturePool pool = CreatePool(out _, out _);
        var keepA = (FakeHandle)pool.Allocate(KeyA, null, "keep_a");
        var dropA = (FakeHandle)pool.Allocate(KeyA, null, "drop_a");
        var dropB = (FakeHandle)pool.Allocate(KeyB, null, "drop_b");
        pool.BeginFrame();

        int pruned = pool.PruneExcept(new HashSet<object> { keepA });

        Assert.That(pruned, Is.EqualTo(2));
        Assert.That(keepA.Disposed, Is.False, "Kept entries survive.");
        Assert.That(dropA.Disposed, Is.True);
        Assert.That(dropB.Disposed, Is.True);
        Assert.That(pool.TotalCountFor(KeyA), Is.EqualTo(1));
        Assert.That(pool.TotalCountFor(KeyB), Is.EqualTo(0), "The emptied key state is removed.");
        Assert.That(pool.TotalCount, Is.EqualTo(1));
        Assert.That(pool.IdleCountFor(KeyA), Is.EqualTo(1), "Surviving entries are idle for the next walk.");

        object rerented = pool.Allocate(KeyA, keepA, "ignored");
        Assert.That(ReferenceEquals(rerented, keepA), Is.True, "The pool keeps working after a prune.");
    }

    [Test(Description = "Entries of one key are invisible to allocations of a different key")]
    public void DifferentKeysAreIsolated()
    {
        using RenderGraphTexturePool pool = CreatePool(out List<TexturePoolKey> keys, out _);
        object a = pool.Allocate(KeyA, null, "a");

        object b = pool.Allocate(KeyB, null, "b");

        Assert.That(ReferenceEquals(b, a), Is.False, "A different key must miss even when another key has entries.");
        Assert.That(keys, Is.EqualTo(new[] { KeyA, KeyB }), "Both allocations missed and invoked the factory.");
        Assert.That(pool.TotalCountFor(KeyA), Is.EqualTo(1));
        Assert.That(pool.TotalCountFor(KeyB), Is.EqualTo(1));

        pool.BeginFrame();
        object b2 = pool.Allocate(KeyB, null, "ignored");
        Assert.That(ReferenceEquals(b2, b), Is.True, "Key B idles its own entry.");
        Assert.That(pool.IdleCountFor(KeyA), Is.EqualTo(1), "Key A's entry is untouched by key B's walk.");
    }
}
