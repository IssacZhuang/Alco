using System;
using System.Threading;

namespace Alco.Test;

/// <summary>Covers DAG scheduling, dual lanes, range batches, reclamation, error handling, and allocation behavior.</summary>
public class TestJobScheduler
{
    /// <summary>Verifies that mixed single, batch, and range-batch submissions execute once per index and run callbacks.</summary>
    [Test]
    public void TestMixedJobsAndCallbacks()
    {
        using var scheduler = new JobScheduler();
        var counts = new int[17];
        var otherCounts = new int[5];
        var rangeCounts = new int[29];
        int singles = 0;
        int singleCallbacks = 0;
        int batchCallbacks = 0;
        int otherCallbacks = 0;
        int rangeCallbacks = 0;

        var single = new SingleJob(() => Interlocked.Increment(ref singles));
        var batch = new BatchJob(counts.Length, index => Interlocked.Increment(ref counts[index]));
        var other = new BatchJob(otherCounts.Length, index => Interlocked.Increment(ref otherCounts[index]));
        var range = new BatchRangeJob(rangeCounts.Length, (start, count) =>
        {
            for (int i = start; i < start + count; i++)
            {
                Interlocked.Increment(ref rangeCounts[i]);
            }
        });

        JobDelegate singleComplete = () => Assert.That(singles, Is.EqualTo(++singleCallbacks));
        JobDelegate batchComplete = () => Assert.That(counts, Is.All.EqualTo(++batchCallbacks));
        JobDelegate otherComplete = () => Assert.That(otherCounts, Is.All.EqualTo(++otherCallbacks));
        JobDelegate rangeComplete = () => Assert.That(rangeCounts, Is.All.EqualTo(++rangeCallbacks));

        scheduler.WaitAll();
        for (int i = 0; i < 10; i++)
        {
            scheduler.Schedule(single, onJobComplete: singleComplete);
            scheduler.ScheduleBatch(batch, onJobComplete: batchComplete);
            scheduler.ScheduleBatch(other, onJobComplete: otherComplete);
            scheduler.ScheduleBatch(range, onJobComplete: rangeComplete);
            scheduler.WaitAll();
        }

        Assert.That(singleCallbacks, Is.EqualTo(10));
        Assert.That(batchCallbacks, Is.EqualTo(10));
        Assert.That(otherCallbacks, Is.EqualTo(10));
        Assert.That(rangeCallbacks, Is.EqualTo(10));
    }

    /// <summary>Verifies that an empty batch consumes zero slots without a callback and one slot with a callback.</summary>
    [Test]
    public void TestEmptyBatch()
    {
        using var scheduler = new JobScheduler { Capacity = 1 };
        var emptyBatch = new BatchJob(0, static _ => Assert.Fail("An empty batch must not execute any indices."));
        var emptyRange = new BatchRangeJob(0, static (_, _) => Assert.Fail("An empty range batch must not execute."));
        int callbacks = 0;
        int singleRan = 0;

        JobHandle h0 = scheduler.ScheduleBatch(emptyBatch);
        JobHandle h1 = scheduler.ScheduleBatch(emptyRange);
        Assert.That(h0.IsValid, Is.False);
        Assert.That(h1.IsValid, Is.False);

        // Zero slots consumed above, so one real slot is still available.
        JobHandle hCallback = scheduler.ScheduleBatch(emptyBatch, onJobComplete: () => Interlocked.Increment(ref callbacks));
        Assert.That(hCallback.IsValid, Is.True);
        scheduler.Complete(hCallback);
        Assert.That(callbacks, Is.EqualTo(1));

        JobHandle hRangeCallback = scheduler.ScheduleBatch(emptyRange, onJobComplete: () => Interlocked.Increment(ref callbacks));
        scheduler.Complete(hRangeCallback);
        Assert.That(callbacks, Is.EqualTo(2));

        JobHandle hSingle = scheduler.Schedule(new SingleJob(() => Interlocked.Increment(ref singleRan)));
        scheduler.Complete(hSingle);
        Assert.That(singleRan, Is.EqualTo(1));
    }

    /// <summary>Verifies exact once-per-index coverage for IBatchRangeJob across edge sizes and explicit batch sizes.</summary>
    [Test]
    public void TestBatchRangeCoverageAcrossSizes()
    {
        using var scheduler = new JobScheduler();
        int[] totalSizes = [1, 7, 12, 13, 100, 1000];
        int[] batchSizes = [0, 1, 3, 17, 5000];

        foreach (int total in totalSizes)
        {
            var hits = new int[total];
            var rangeJob = new BatchRangeJob(total, (start, count) =>
            {
                for (int i = start; i < start + count; i++)
                {
                    Interlocked.Increment(ref hits[i]);
                }
            });

            foreach (int bs in batchSizes)
            {
                Array.Clear(hits);
                JobHandle h = scheduler.ScheduleBatch(rangeJob, batchSize: bs);
                scheduler.Complete(h);
                Assert.That(hits, Is.All.EqualTo(1), $"Mismatch for total={total}, batchSize={bs}");
            }
        }
    }

    /// <summary>Verifies that a capacity error does not queue a partial job or prevent resizing after WaitAll.</summary>
    [Test]
    public void TestCapacityDoesNotGrowDuringScheduling()
    {
        using var scheduler = new JobScheduler { Capacity = 1 };
        int count = 0;
        using var gate = new ManualResetEventSlim();
        using var started = new ManualResetEventSlim();
        var blocking = new SingleJob(() =>
        {
            started.Set();
            gate.Wait(TimeSpan.FromSeconds(10));
        });
        var batch = new BatchJob(1, _ => Interlocked.Increment(ref count));

        scheduler.Schedule(blocking, lane: JobLane.Background);
        Assert.That(started.Wait(TimeSpan.FromSeconds(10)), Is.True);

        Assert.Throws<InvalidOperationException>(() => scheduler.ScheduleBatch(batch));
        Assert.Throws<InvalidOperationException>(() => scheduler.Capacity = 2);

        gate.Set();
        scheduler.WaitAll();
        Assert.That(count, Is.Zero);

        scheduler.Capacity = 2;
        scheduler.Schedule(new SingleJob(() => { }));
        scheduler.ScheduleBatch(batch);
        scheduler.WaitAll();
        Assert.That(count, Is.EqualTo(1));
    }

    /// <summary>Verifies linear chains, diamonds, multi-parent fan-in, and already-completed dependencies.</summary>
    [Test]
    public void TestDagDependencies()
    {
        using var scheduler = new JobScheduler();

        // Linear chain A -> B -> C -> D
        int step = 0;
        int sA = 0, sB = 0, sC = 0, sD = 0;
        JobHandle hA = scheduler.Schedule(new SingleJob(() => sA = Interlocked.Increment(ref step)));
        JobHandle hB = scheduler.Schedule(new SingleJob(() => sB = Interlocked.Increment(ref step)), dependsOn: hA);
        JobHandle hC = scheduler.Schedule(new SingleJob(() => sC = Interlocked.Increment(ref step)), dependsOn: hB);
        JobHandle hD = scheduler.Schedule(new SingleJob(() => sD = Interlocked.Increment(ref step)), dependsOn: hC);
        scheduler.Complete(hD);

        Assert.That(sA, Is.EqualTo(1));
        Assert.That(sB, Is.EqualTo(2));
        Assert.That(sC, Is.EqualTo(3));
        Assert.That(sD, Is.EqualTo(4));

        // Diamond A -> {B, C} -> D
        int dA = 0, dB = 0, dC = 0, dD = 0;
        int dOrder = 0;
        JobHandle root = scheduler.Schedule(new SingleJob(() => dA = Interlocked.Increment(ref dOrder)));
        JobHandle left = scheduler.Schedule(new SingleJob(() => dB = Interlocked.Increment(ref dOrder)), dependsOn: root);
        JobHandle right = scheduler.Schedule(new SingleJob(() => dC = Interlocked.Increment(ref dOrder)), dependsOn: root);
        JobHandle join = scheduler.CombineDependencies(left, right);
        JobHandle tail = scheduler.Schedule(new SingleJob(() => dD = Interlocked.Increment(ref dOrder)), dependsOn: join);
        scheduler.Complete(tail);

        Assert.That(dA, Is.EqualTo(1));
        Assert.That(dB, Is.InRange(2, 3));
        Assert.That(dC, Is.InRange(2, 3));
        Assert.That(dD, Is.EqualTo(4));

        // 8-parent fan-in via ReadOnlySpan<JobHandle>
        int parentMask = 0;
        int observedMask = 0;
        Span<JobHandle> parents = stackalloc JobHandle[8];
        for (int i = 0; i < parents.Length; i++)
        {
            int bit = 1 << i;
            parents[i] = scheduler.Schedule(new SingleJob(() => Interlocked.Or(ref parentMask, bit)));
        }
        JobHandle fanIn = scheduler.CombineDependencies(parents);
        JobHandle consumer = scheduler.Schedule(new SingleJob(() => observedMask = Volatile.Read(ref parentMask)), dependsOn: fanIn);
        scheduler.Complete(consumer);
        Assert.That(observedMask, Is.EqualTo(0xFF));

        // Already-completed parent dependency
        int afterDone = 0;
        JobHandle alreadyDone = scheduler.Schedule(new SingleJob(() => { }));
        scheduler.Complete(alreadyDone);
        JobHandle next = scheduler.Schedule(new SingleJob(() => afterDone = 42), dependsOn: alreadyDone);
        scheduler.Complete(next);
        Assert.That(afterDone, Is.EqualTo(42));
    }

    /// <summary>Stress-tests fast parent completion racing against dynamic dependent attachment.</summary>
    [Test]
    public void TestDagAttachRaceStress()
    {
        using var scheduler = new JobScheduler();
        int[] order = new int[2];

        for (int iter = 0; iter < 2000; iter++)
        {
            order[0] = 0;
            order[1] = 0;

            JobHandle p1 = scheduler.Schedule(new SingleJob(() => order[0] = 1));
            JobHandle p2 = scheduler.Schedule(new SingleJob(() => order[1] = 1));
            JobHandle combined = scheduler.CombineDependencies(p1, p2);
            int childSum = 0;
            JobHandle child = scheduler.Schedule(new SingleJob(() => childSum = order[0] + order[1]), dependsOn: combined);
            scheduler.Complete(child);

            Assert.That(childSum, Is.EqualTo(2), $"Race detected at iteration {iter}");
        }
    }

    /// <summary>Verifies selective Complete, IsCompleted polling, idempotency, and stale handles after reuse.</summary>
    [Test]
    public void TestSelectiveCompleteAndStaleHandle()
    {
        using var scheduler = new JobScheduler { Capacity = 2 };
        using var release = new ManualResetEventSlim();
        using var started = new ManualResetEventSlim();

        JobHandle slow = scheduler.Schedule(new SingleJob(() =>
        {
            started.Set();
            release.Wait(TimeSpan.FromSeconds(10));
        }), lane: JobLane.Background);

        Assert.That(started.Wait(TimeSpan.FromSeconds(10)), Is.True);
        Assert.That(scheduler.IsCompleted(slow), Is.False);

        int fastVal = 0;
        JobHandle fast = scheduler.Schedule(new SingleJob(() => fastVal = 7));
        scheduler.Complete(fast);
        Assert.That(fastVal, Is.EqualTo(7));
        Assert.That(scheduler.IsCompleted(fast), Is.True);

        // Idempotent second Complete on the same handle
        scheduler.Complete(fast);

        // Reuse the reclaimed slot while slow is still running; old handle must remain stale/completed.
        int secondVal = 0;
        JobHandle reused = scheduler.Schedule(new SingleJob(() => secondVal = 9));
        Assert.That(scheduler.IsCompleted(fast), Is.True);
        scheduler.Complete(reused);
        Assert.That(secondVal, Is.EqualTo(9));

        release.Set();
        scheduler.Complete(slow);
        Assert.That(scheduler.IsCompleted(slow), Is.True);
    }

    /// <summary>Verifies that Frame and Background lanes synchronize independently and support cross-lane dependencies.</summary>
    [Test]
    public void TestDualLaneIsolationAndCrossLaneDependencies()
    {
        using var scheduler = new JobScheduler(frameWorkerCount: 2, backgroundWorkerCount: 1);
        using var bgStarted = new ManualResetEventSlim();
        using var bgRelease = new ManualResetEventSlim();

        int bgFinished = 0;
        int frameFinished = 0;

        JobHandle bgHandle = scheduler.Schedule(new SingleJob(() =>
        {
            bgStarted.Set();
            bgRelease.Wait(TimeSpan.FromSeconds(10));
            Volatile.Write(ref bgFinished, 1);
        }), lane: JobLane.Background);

        Assert.That(bgStarted.Wait(TimeSpan.FromSeconds(10)), Is.True);

        scheduler.Schedule(new SingleJob(() => Volatile.Write(ref frameFinished, 1)), lane: JobLane.Frame);
        scheduler.WaitAll(JobLane.Frame);

        Assert.That(Volatile.Read(ref frameFinished), Is.EqualTo(1));
        Assert.That(Volatile.Read(ref bgFinished), Is.Zero);
        Assert.That(scheduler.IsCompleted(bgHandle), Is.False);

        bgRelease.Set();
        scheduler.WaitAll(JobLane.Background);
        Assert.That(Volatile.Read(ref bgFinished), Is.EqualTo(1));

        // Cross-lane dependencies: Frame -> Background -> Frame
        int phase = 0;
        int p1 = 0, p2 = 0, p3 = 0;
        JobHandle f1 = scheduler.Schedule(new SingleJob(() => p1 = Interlocked.Increment(ref phase)), lane: JobLane.Frame);
        JobHandle b1 = scheduler.Schedule(new SingleJob(() => p2 = Interlocked.Increment(ref phase)), dependsOn: f1, lane: JobLane.Background);
        JobHandle f2 = scheduler.Schedule(new SingleJob(() => p3 = Interlocked.Increment(ref phase)), dependsOn: b1, lane: JobLane.Frame);
        scheduler.Complete(f2);

        Assert.That(p1, Is.EqualTo(1));
        Assert.That(p2, Is.EqualTo(2));
        Assert.That(p3, Is.EqualTo(3));
    }

    /// <summary>Verifies lazy slot and dependency edge reclamation across long chains on small capacity.</summary>
    [Test]
    public void TestLazyReclamationWithSmallCapacity()
    {
        using var scheduler = new JobScheduler { Capacity = 16 };
        int counter = 0;
        JobHandle prev = default;

        for (int i = 0; i < 2000; i++)
        {
            prev = scheduler.Schedule(new SingleJob(() => Interlocked.Increment(ref counter)), dependsOn: prev);
            if ((i & 7) == 7)
            {
                // Periodically drain only one handle to keep active window well below 16.
                scheduler.Complete(prev);
                prev = default;
            }
        }

        scheduler.Complete(prev);
        Assert.That(counter, Is.EqualTo(2000));
        scheduler.WaitAll();
    }

    /// <summary>Verifies error reporting across single, batch, range-batch, callback, and uncompleted ancestor jobs.</summary>
    [Test]
    public void TestErrorPropagationAndReuse()
    {
        using var scheduler = new JobScheduler();
        var singleError = new InvalidOperationException("single");
        var batchError = new InvalidOperationException("batch");
        var rangeError = new InvalidOperationException("range");
        var callbackError = new InvalidOperationException("callback");
        var ancestorError = new InvalidOperationException("ancestor");

        int callbacks = 0;
        JobDelegate complete = () => Interlocked.Increment(ref callbacks);

        scheduler.Schedule(new SingleJob(() => throw singleError), onJobComplete: complete);
        scheduler.ScheduleBatch(new BatchJob(17, i => { if (i == 8) throw batchError; }), onJobComplete: complete);
        scheduler.ScheduleBatch(new BatchRangeJob(32, (s, c) => { if (s <= 10 && s + c > 10) throw rangeError; }), batchSize: 4, onJobComplete: complete);
        scheduler.ScheduleBatch(new BatchJob(5, static _ => { }), onJobComplete: () => throw callbackError);

        var err = Assert.Throws<AggregateException>(scheduler.WaitAll);
        Assert.That(err!.InnerExceptions, Is.EquivalentTo(new[] { singleError, batchError, rangeError, callbackError }));
        Assert.That(callbacks, Is.Zero);

        // Uncompleted ancestor error surfaced through Complete(child) and consumed once.
        JobHandle badParent = scheduler.Schedule(new SingleJob(() => throw ancestorError));
        JobHandle child = scheduler.Schedule(new SingleJob(() => { }), dependsOn: badParent);
        var childErr = Assert.Throws<AggregateException>(() => scheduler.Complete(child));
        Assert.That(childErr!.InnerExceptions, Is.EquivalentTo(new[] { ancestorError }));

        // Scheduler must remain clean and reusable afterwards.
        scheduler.WaitAll();
        int ok = 0;
        JobHandle good = scheduler.Schedule(new SingleJob(() => ok = 1), onJobComplete: complete);
        scheduler.Complete(good);
        Assert.That(ok, Is.EqualTo(1));
        Assert.That(callbacks, Is.EqualTo(1));
    }

    /// <summary>Verifies zero managed allocations on the producer thread when reusing job and delegate instances.</summary>
    [Test]
    public void TestProducerZeroAllocations()
    {
        using var scheduler = new JobScheduler();
        var single = new SingleJob(static () => { });
        var batch = new BatchJob(32, static _ => { });
        var range = new BatchRangeJob(64, static (_, _) => { });
        JobDelegate onJobComplete = static () => { };

        RunZeroAllocLoop(scheduler, single, batch, range, onJobComplete, 500);

        long before = GC.GetAllocatedBytesForCurrentThread();
        RunZeroAllocLoop(scheduler, single, batch, range, onJobComplete, 2000);
        long after = GC.GetAllocatedBytesForCurrentThread();

        Assert.That(after - before, Is.Zero);
    }

    private static void RunZeroAllocLoop(
        JobScheduler scheduler,
        IJob single,
        IBatchJob batch,
        IBatchRangeJob range,
        JobDelegate onJobComplete,
        int repetitions)
    {
        for (int i = 0; i < repetitions; i++)
        {
            JobHandle h0 = scheduler.Schedule(single, onJobComplete: onJobComplete);
            JobHandle h1 = scheduler.ScheduleBatch(batch, onJobComplete: onJobComplete);
            JobHandle h2 = scheduler.ScheduleBatch(range, onJobComplete: onJobComplete);
            JobHandle hCombined = scheduler.CombineDependencies(h0, h1, h2);
            scheduler.Complete(hCombined);
            scheduler.WaitAll(JobLane.Frame);
        }
    }

    private sealed class SingleJob(JobDelegate execute) : IJob
    {
        void IJob.Execute() => execute();
    }

    private sealed class BatchJob(int count, ParallelForDelegate execute) : IBatchJob
    {
        public int Count { get; } = count;
        void IBatchJob.Execute(int index) => execute(index);
    }

    private sealed class BatchRangeJob(int count, Action<int, int> execute) : IBatchRangeJob
    {
        public int Count { get; } = count;
        void IBatchRangeJob.Execute(int start, int count) => execute(start, count);
    }
}
