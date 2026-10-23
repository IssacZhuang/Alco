using System.Collections.Generic;
using NUnit.Framework;

namespace Alco.Graphics.Test;

/// <summary>
/// Unit tests for <see cref="ReadbackStagingBufferCachePolicy{TTicket}"/>. These exercise the
/// pure-managed selection and trimming logic with no native WebGPU handles, using a synthetic
/// clock and opaque ticket values.
/// </summary>
[TestFixture]
public class ReadbackStagingBufferCachePolicyTests
{
    private const ulong OneMegabyte = 1UL << 20;

    // Policy sized for readable assertions: 8 MB budget, 4 MB single-buffer max,
    // 10 ticks expiration, 1 MB oversize reuse threshold.
    private static ReadbackStagingBufferCachePolicy<int> CreatePolicy()
    {
        return new ReadbackStagingBufferCachePolicy<int>(
            idleBudget: 8UL * OneMegabyte,
            singleBufferMax: 4UL * OneMegabyte,
            idleExpirationTicks: 10,
            oversizeReuseThresholdBytes: OneMegabyte);
    }

    /// <summary>Tests that bucketization rounds sizes up to powers of two or 1 MB steps.</summary>
    [Test(Description = "Bucketize rounds up small sizes to powers of two and large sizes to 1 MB steps")]
    public void BucketizeRoundsUp()
    {
        ReadbackStagingBufferCachePolicy<int> policy = CreatePolicy();

        Assert.That(policy.Bucketize(1), Is.EqualTo(1));
        Assert.That(policy.Bucketize(3), Is.EqualTo(4));
        Assert.That(policy.Bucketize(5), Is.EqualTo(8));
        Assert.That(policy.Bucketize(OneMegabyte), Is.EqualTo(OneMegabyte));
        Assert.That(policy.Bucketize(OneMegabyte + 1), Is.EqualTo(2UL * OneMegabyte));
        Assert.That(policy.Bucketize(3UL * OneMegabyte), Is.EqualTo(3UL * OneMegabyte));
        Assert.That(policy.Bucketize(3UL * OneMegabyte + 1), Is.EqualTo(4UL * OneMegabyte));
    }

    /// <summary>Tests that acquiring from an empty cache fails.</summary>
    [Test(Description = "TryAcquire returns false on an empty cache")]
    public void TryAcquireEmptyReturnsFalse()
    {
        ReadbackStagingBufferCachePolicy<int> policy = CreatePolicy();

        Assert.That(policy.TryAcquire(OneMegabyte, out int ticket), Is.False);
        Assert.That(ticket, Is.EqualTo(0));
        Assert.That(policy.IdleCount, Is.EqualTo(0));
    }

    /// <summary>Tests that a returned buffer is reused at the same size.</summary>
    [Test(Description = "A returned buffer can be acquired again at the same size")]
    public void ReturnThenAcquireReusesBuffer()
    {
        ReadbackStagingBufferCachePolicy<int> policy = CreatePolicy();

        policy.Return(ticket: 42, capacity: OneMegabyte, nowTimestamp: 0, evicted: new List<int>());

        Assert.That(policy.IdleCount, Is.EqualTo(1));
        Assert.That(policy.IdleCapacityBytes, Is.EqualTo(OneMegabyte));

        Assert.That(policy.TryAcquire(OneMegabyte, out int ticket), Is.True);
        Assert.That(ticket, Is.EqualTo(42));
        Assert.That(policy.IdleCount, Is.EqualTo(0));
        Assert.That(policy.IdleCapacityBytes, Is.EqualTo(0UL));
    }

    /// <summary>Tests that acquiring picks the smallest sufficient buffer.</summary>
    [Test(Description = "TryAcquire picks the smallest sufficient cached buffer (best-fit)")]
    public void TryAcquirePicksBestFit()
    {
        ReadbackStagingBufferCachePolicy<int> policy = CreatePolicy();
        List<int> evicted = new();
        policy.Return(1, capacity: OneMegabyte, nowTimestamp: 0, evicted);
        policy.Return(2, capacity: 2UL * OneMegabyte, nowTimestamp: 0, evicted);
        policy.Return(3, capacity: 3UL * OneMegabyte, nowTimestamp: 0, evicted);

        // Request 1.5 MB: candidates are 2 MB and 3 MB. Best fit is 2 MB.
        Assert.That(policy.TryAcquire(OneMegabyte + OneMegabyte / 2, out int ticket), Is.True);
        Assert.That(ticket, Is.EqualTo(2));
    }

    /// <summary>Tests that acquiring rejects excessively oversized buffers.</summary>
    [Test(Description = "TryAcquire rejects buffers that are excessively oversized")]
    public void TryAcquireRejectsOversized()
    {
        ReadbackStagingBufferCachePolicy<int> policy = CreatePolicy();
        List<int> evicted = new();
        // Cache a 3 MB buffer. Oversize ceiling for a 1 MB request is max(2 MB, 2 MB) = 2 MB,
        // so the 3 MB buffer must not be handed out.
        policy.Return(99, capacity: 3UL * OneMegabyte, nowTimestamp: 0, evicted);

        Assert.That(policy.TryAcquire(OneMegabyte, out int _), Is.False);
        Assert.That(policy.IdleCount, Is.EqualTo(1), "Oversized buffer stays cached for a later large read");

        // A 3 MB request can reuse it.
        Assert.That(policy.TryAcquire(3UL * OneMegabyte, out int ticket), Is.True);
        Assert.That(ticket, Is.EqualTo(99));
    }

    /// <summary>Tests that the oversize ceiling uses the threshold for small requests.</summary>
    [Test(Description = "TryAcquire uses the larger of required*2 and required+threshold for the oversize ceiling")]
    public void TryAcquireOversizeCeilingUsesThresholdForSmallRequests()
    {
        // With threshold = 1 MB, a tiny request of 64 KB should reuse up to 64 KB + 1 MB,
        // which is larger than 64 KB * 2. So a 1 MB buffer (within 1.06 MB ceiling) is accepted.
        ReadbackStagingBufferCachePolicy<int> policy = CreatePolicy();
        List<int> evicted = new();
        policy.Return(7, capacity: OneMegabyte, nowTimestamp: 0, evicted);

        Assert.That(policy.TryAcquire(64UL * 1024, out int ticket), Is.True);
        Assert.That(ticket, Is.EqualTo(7));
    }

    /// <summary>Tests that buffers above the single-buffer maximum are never cached.</summary>
    [Test(Description = "Oversized buffers (above singleBufferMax) are never cached on return")]
    public void OversizedBuffersAreNeverCached()
    {
        ReadbackStagingBufferCachePolicy<int> policy = CreatePolicy();
        List<int> evicted = new();

        // 8 MB exceeds the 4 MB single-buffer max.
        policy.Return(ticket: 5, capacity: 8UL * OneMegabyte, nowTimestamp: 0, evicted);

        Assert.That(policy.IdleCount, Is.EqualTo(0));
        Assert.That(policy.IdleCapacityBytes, Is.EqualTo(0UL));
        Assert.That(evicted, Is.Empty, "Oversized buffers are not added nor evicted; the caller keeps the handle to destroy");
    }

    /// <summary>Tests that cache eligibility reflects the single-buffer maximum.</summary>
    [Test(Description = "ShouldCacheOnReturn reports oversized buffers as non-cacheable")]
    public void ShouldCacheOnReturnReflectsSingleBufferMax()
    {
        ReadbackStagingBufferCachePolicy<int> policy = CreatePolicy();

        Assert.That(policy.ShouldCacheOnReturn(OneMegabyte), Is.True);
        Assert.That(policy.ShouldCacheOnReturn(4UL * OneMegabyte), Is.True);
        Assert.That(policy.ShouldCacheOnReturn(4UL * OneMegabyte + 1), Is.False);
    }

    /// <summary>Tests that trimming evicts entries expired by age.</summary>
    [Test(Description = "Trim evicts entries that have expired by age even when under budget")]
    public void TrimEvictsExpiredByAge()
    {
        ReadbackStagingBufferCachePolicy<int> policy = CreatePolicy();
        List<int> evicted = new();
        policy.Return(1, OneMegabyte, nowTimestamp: 0, evicted);
        policy.Return(2, OneMegabyte, nowTimestamp: 5, evicted);

        // At timestamp 12: entry 1 (age 12) and entry 2 (age 7). Expiration is 10 ticks,
        // so entry 1 expires; entry 2 survives.
        policy.Trim(nowTimestamp: 12, evicted);

        Assert.That(evicted, Is.EqualTo(new[] { 1 }));
        Assert.That(policy.IdleCount, Is.EqualTo(1));
        Assert.That(policy.IdleCapacityBytes, Is.EqualTo(OneMegabyte));
    }

    /// <summary>Tests that trimming evicts the oldest entries when over budget.</summary>
    [Test(Description = "Trim evicts oldest first when total capacity exceeds the budget")]
    public void TrimEvictsOldestWhenOverBudget()
    {
        ReadbackStagingBufferCachePolicy<int> policy = CreatePolicy();
        List<int> evicted = new();

        // Budget is 8 MB. Insert three 4 MB buffers at increasing timestamps.
        policy.Return(11, 4UL * OneMegabyte, nowTimestamp: 0, evicted);
        policy.Return(12, 4UL * OneMegabyte, nowTimestamp: 1, evicted);
        // Now at 8 MB (the budget). A third pushes to 12 MB -> over budget.
        policy.Return(13, 4UL * OneMegabyte, nowTimestamp: 2, evicted);

        // The Return above already trims; expect the oldest (11) evicted to stay at budget.
        Assert.That(evicted, Does.Contain(11));
        Assert.That(policy.IdleCapacityBytes, Is.LessThanOrEqualTo(8UL * OneMegabyte));
    }

    /// <summary>Tests that draining returns all tickets and resets capacity.</summary>
    [Test(Description = "Drain returns all tickets and resets capacity")]
    public void DrainClearsAll()
    {
        ReadbackStagingBufferCachePolicy<int> policy = CreatePolicy();
        List<int> evicted = new();
        policy.Return(1, OneMegabyte, 0, evicted);
        policy.Return(2, 2UL * OneMegabyte, 0, evicted);

        List<int> drained = new();
        policy.Drain(drained);

        CollectionAssert.AreEquivalent(new[] { 1, 2 }, drained);
        Assert.That(policy.IdleCount, Is.EqualTo(0));
        Assert.That(policy.IdleCapacityBytes, Is.EqualTo(0UL));
    }

    /// <summary>Tests that capacity accounting stays consistent across acquire and return.</summary>
    [Test(Description = "Capacity accounting stays consistent across acquire/return cycles")]
    public void CapacityAccountingConsistent()
    {
        ReadbackStagingBufferCachePolicy<int> policy = CreatePolicy();
        List<int> evicted = new();

        policy.Return(1, 2UL * OneMegabyte, 0, evicted);
        policy.Return(2, OneMegabyte, 0, evicted);

        ulong afterReturns = policy.IdleCapacityBytes;
        Assert.That(afterReturns, Is.EqualTo(3UL * OneMegabyte));

        Assert.That(policy.TryAcquire(OneMegabyte, out int _), Is.True);
        Assert.That(policy.IdleCapacityBytes, Is.EqualTo(2UL * OneMegabyte));

        Assert.That(policy.TryAcquire(OneMegabyte, out int _), Is.True);
        Assert.That(policy.IdleCapacityBytes, Is.EqualTo(0UL));
        Assert.That(policy.IdleCount, Is.EqualTo(0));
    }
}
