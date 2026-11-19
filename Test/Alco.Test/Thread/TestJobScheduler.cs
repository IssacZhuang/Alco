using System;
using System.Threading;

namespace Alco.Test;

/// <summary>Covers reusable dispatch, completion, and allocation behavior.</summary>
public class TestJobScheduler
{
    /// <summary>Verifies that mixed submissions execute once per index and complete on pool workers.</summary>
    [Test]
    public void TestMixedJobsAndCallbacks()
    {
        using var scheduler = new JobScheduler();
        var counts = new int[17];
        var otherCounts = new int[5];
        int singles = 0;
        int singleCallbacks = 0;
        int batchCallbacks = 0;
        int otherCallbacks = 0;
        var single = new SingleJob(() => singles++);
        var batch = new BatchJob(counts.Length, index => Interlocked.Increment(ref counts[index]));
        var other = new BatchJob(otherCounts.Length, index => Interlocked.Increment(ref otherCounts[index]));
        JobDelegate singleComplete = () =>
        {
            Assert.That(Thread.CurrentThread.IsThreadPoolThread, Is.True);
            Assert.That(singles, Is.EqualTo(++singleCallbacks));
        };
        JobDelegate batchComplete = () =>
        {
            Assert.That(Thread.CurrentThread.IsThreadPoolThread, Is.True);
            Assert.That(counts, Is.All.EqualTo(++batchCallbacks));
        };
        JobDelegate otherComplete = () => Assert.That(otherCounts, Is.All.EqualTo(++otherCallbacks));

        scheduler.WaitAll();
        for (int i = 0; i < 10; i++)
        {
            scheduler.Schedule(single, singleComplete);
            scheduler.Schedule(batch, batchComplete);
            scheduler.Schedule(other, otherComplete);
            scheduler.WaitAll();
        }

        Assert.That(singleCallbacks, Is.EqualTo(10));
        Assert.That(batchCallbacks, Is.EqualTo(10));
        Assert.That(otherCallbacks, Is.EqualTo(10));
    }

    /// <summary>Verifies that an empty batch consumes a slot only for a supplied callback.</summary>
    [Test]
    public void TestEmptyBatch()
    {
        using var scheduler = new JobScheduler { Capacity = 1 };
        var batch = new BatchJob(0, static _ => Assert.Fail("An empty job must not execute any indices."));
        int callbacks = 0;
        scheduler.Schedule(batch);
        scheduler.Schedule(batch, () => callbacks++);
        scheduler.WaitAll();

        Assert.That(callbacks, Is.EqualTo(1));
    }

    /// <summary>Verifies that a capacity error does not queue a partial batch or prevent reuse.</summary>
    [Test]
    public void TestCapacityDoesNotGrowDuringScheduling()
    {
        using var scheduler = new JobScheduler { Capacity = 1 };
        int count = 0;
        var single = new SingleJob(static () => { });
        var batch = new BatchJob(1, _ => count++);
        scheduler.Schedule(single);

        Assert.Throws<InvalidOperationException>(() => scheduler.Schedule(batch));
        Assert.Throws<InvalidOperationException>(() => scheduler.Capacity = 2);
        scheduler.WaitAll();
        Assert.That(count, Is.Zero);

        scheduler.Capacity = 2;
        scheduler.Schedule(single);
        scheduler.Schedule(batch);
        scheduler.WaitAll();
        Assert.That(count, Is.EqualTo(1));
    }

    /// <summary>Verifies that a callback can block one instance without holding up a second one.</summary>
    [Test]
    public void TestWaitAllIsPerInstance()
    {
        var timeout = TimeSpan.FromSeconds(10);
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var first = new JobScheduler();
        using var second = new JobScheduler();
        var job = new SingleJob(static () => { });
        int callbacks = 0;
        first.Schedule(job, () =>
        {
            started.Set();
            if (!release.Wait(timeout))
                throw new TimeoutException("The first scheduler's callback was not released.");
        });
        try
        {
            Assert.That(started.Wait(timeout), Is.True);
            second.Schedule(job, () => callbacks++);
            second.WaitAll();
            Assert.That(callbacks, Is.EqualTo(1));
        }
        finally
        {
            release.Set();
        }
        first.WaitAll();
    }

    /// <summary>Verifies that the barrier reports job and callback errors and leaves reusable slots.</summary>
    [Test]
    public void TestWaitAllReportsErrorsAndAllowsReuse()
    {
        using var scheduler = new JobScheduler();
        var singleError = new InvalidOperationException();
        var batchError = new InvalidOperationException();
        var callbackError = new InvalidOperationException();
        var batch = new BatchJob(17, index =>
        {
            if (index == 8) throw batchError;
        });
        var success = new BatchJob(5, static _ => { });
        int callbacks = 0;
        JobDelegate complete = () => callbacks++;

        scheduler.Schedule(new SingleJob(() => throw singleError), complete);
        scheduler.Schedule(batch, complete);
        scheduler.Schedule(success, () => throw callbackError);

        var error = Assert.Throws<AggregateException>(scheduler.WaitAll);
        Assert.That(error!.InnerExceptions, Is.EquivalentTo(new[] { singleError, batchError, callbackError }));
        Assert.That(callbacks, Is.Zero);

        scheduler.WaitAll();
        scheduler.Schedule(success, complete);
        scheduler.WaitAll();
        Assert.That(callbacks, Is.EqualTo(1));
    }

    /// <summary>Verifies that disposal joins pending work and prevents further scheduling.</summary>
    [Test]
    public void TestDisposeWaitsForCallbacks()
    {
        using var scheduler = new JobScheduler();
        int callbacks = 0;
        var job = new SingleJob(static () => { });
        scheduler.Schedule(job, () => callbacks++);
        scheduler.Dispose();

        Assert.That(callbacks, Is.EqualTo(1));
        Assert.Throws<ObjectDisposedException>(() => scheduler.Schedule(job));
        Assert.Throws<ObjectDisposedException>(scheduler.WaitAll);
    }

    /// <summary>Verifies zero managed allocations on the producer when reusing jobs and callbacks.</summary>
    [Test]
    public void TestScheduleAndWaitAllocateNothingOnProducer()
    {
        using var scheduler = new JobScheduler();
        var single = new SingleJob(static () => { });
        var batch = new BatchJob(5, static _ => { });
        JobDelegate onJobComplete = static () => { };
        RunRepeated(scheduler, single, batch, onJobComplete, 2_000);

        long before = GC.GetAllocatedBytesForCurrentThread();
        RunRepeated(scheduler, single, batch, onJobComplete, 10_000);
        long after = GC.GetAllocatedBytesForCurrentThread();

        Assert.That(after - before, Is.Zero);
    }

    private static void RunRepeated(JobScheduler scheduler, IJob single, IBatchJob batch, JobDelegate onJobComplete, int repetitions)
    {
        for (int i = 0; i < repetitions; i++)
        {
            scheduler.Schedule(single, onJobComplete);
            scheduler.Schedule(batch, onJobComplete);
            scheduler.WaitAll();
        }
    }

    private sealed class SingleJob(JobDelegate execute) : IJob
    {
        void IJob.Execute() => execute();
    }

    private sealed class BatchJob(int count, ParallelForDelegate execute) : IBatchJob
    {
        /// <inheritdoc/>
        public int Count { get; } = count;

        void IBatchJob.Execute(int index) => execute(index);
    }
}
