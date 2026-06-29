using NUnit.Framework;

namespace Alco.Profiler.Test;

public sealed class MethodProfilerRuntimeTest
{
    private interface IFakeTickable
    {
    }

    private sealed class FakeTickable : IFakeTickable
    {
    }

    private long _timestamp;
    private MethodProfilerRuntime _runtime = null!;

    [SetUp]
    public void SetUp()
    {
        _timestamp = 0;
        _runtime = new MethodProfilerRuntime(() => _timestamp, 1000);
    }

    [Test]
    public void NestedMethodsProduceInclusiveAndSelfTime()
    {
        Register(1);
        Register(2);
        Assert.That(_runtime.TryAcquireSession("test", MethodProfileFilter.All(), out MethodProfilerSession? session, out _), Is.True);
        using (session)
        {
            MethodProfilerTickToken tick = _runtime.BeginTick();
            MethodProfileToken parent = _runtime.Enter(1, MethodProfileTag.None, null);
            _timestamp = 10;
            MethodProfileToken child = _runtime.Enter(2, MethodProfileTag.None, null);
            _timestamp = 30;
            _runtime.Exit(child);
            _timestamp = 50;
            _runtime.Exit(parent);
            _runtime.EndTick(tick);

            Assert.That(_runtime.TryGetLatestSnapshot(out MethodProfilerSnapshot? snapshot), Is.True);
            Assert.That(snapshot, Is.Not.Null);
            MethodProfileSample parentSample = snapshot!.MethodSamples.Span.ToArray().Single(sample => sample.MethodId == 1);
            MethodProfileSample childSample = snapshot.MethodSamples.Span.ToArray().Single(sample => sample.MethodId == 2);
            Assert.Multiple(() =>
            {
                Assert.That(parentSample.Inclusive, Is.EqualTo(TimeSpan.FromMilliseconds(50)));
                Assert.That(parentSample.Self, Is.EqualTo(TimeSpan.FromMilliseconds(30)));
                Assert.That(childSample.Inclusive, Is.EqualTo(TimeSpan.FromMilliseconds(20)));
                Assert.That(childSample.Self, Is.EqualTo(TimeSpan.FromMilliseconds(20)));
            });
        }
    }

    [Test]
    public void SessionIsExclusiveAndReportsOwner()
    {
        Register(1);
        Assert.That(_runtime.TryAcquireSession("first", MethodProfileFilter.All(), out MethodProfilerSession? first, out _), Is.True);
        using (first)
        {
            Assert.That(_runtime.TryAcquireSession("second", MethodProfileFilter.All(), out _, out string? owner), Is.False);
            Assert.That(owner, Is.EqualTo("first"));
        }

        Assert.That(_runtime.TryAcquireSession("second", MethodProfileFilter.All(), out MethodProfilerSession? second, out _), Is.True);
        second!.Dispose();
    }

    [Test]
    public void NestedComponentBodiesProduceOneConcreteTypeAggregate()
    {
        string contract = typeof(IFakeTickable).FullName!;
        Register(1, MethodProfileTag.ComponentTick, contract);
        Register(2, MethodProfileTag.ComponentTick, contract);
        Assert.That(_runtime.TryAcquireSession(
            "component",
            MethodProfileFilter.ByTags(MethodProfileTag.ComponentTick),
            out MethodProfilerSession? session,
            out _), Is.True);

        using (session)
        {
            var context = new FakeTickable();
            MethodProfilerTickToken tick = _runtime.BeginTick();
            MethodProfileToken outer = _runtime.Enter(1, MethodProfileTag.ComponentTick, context);
            _timestamp = 10;
            MethodProfileToken inheritedBase = _runtime.Enter(2, MethodProfileTag.ComponentTick, context);
            _timestamp = 20;
            _runtime.Exit(inheritedBase);
            _timestamp = 30;
            _runtime.Exit(outer);
            _runtime.EndTick(tick);

            _runtime.TryGetLatestSnapshot(out MethodProfilerSnapshot? snapshot);
            MethodProfileContextSample aggregate = snapshot!.ContextSamples.Span[0];
            Assert.Multiple(() =>
            {
                Assert.That(aggregate.ContextType, Is.EqualTo(typeof(FakeTickable)));
                Assert.That(aggregate.Calls, Is.EqualTo(1));
                Assert.That(aggregate.Inclusive, Is.EqualTo(TimeSpan.FromMilliseconds(30)));
            });
        }
    }

    [Test]
    public void NonMatchingRuntimeContractDoesNotCreateComponentAggregate()
    {
        Register(1, MethodProfileTag.ComponentTick, typeof(IFakeTickable).FullName);
        _runtime.TryAcquireSession(
            "component",
            MethodProfileFilter.ByTags(MethodProfileTag.ComponentTick),
            out MethodProfilerSession? session,
            out _);
        using (session)
        {
            MethodProfilerTickToken tick = _runtime.BeginTick();
            MethodProfileToken scope = _runtime.Enter(1, MethodProfileTag.ComponentTick, new object());
            _timestamp = 10;
            _runtime.Exit(scope);
            _runtime.EndTick(tick);
            _runtime.TryGetLatestSnapshot(out MethodProfilerSnapshot? snapshot);
            Assert.That(snapshot!.ContextSamples.IsEmpty, Is.True);
        }
    }

    [Test]
    public void ParallelCallsRemainIsolatedAndMergeAtEndTick()
    {
        long timestamp = 0;
        _runtime = new MethodProfilerRuntime(() => Interlocked.Increment(ref timestamp), 1000);
        Register(1);
        _runtime.TryAcquireSession("parallel", MethodProfileFilter.All(), out MethodProfilerSession? session, out _);
        using (session)
        {
            MethodProfilerTickToken tick = _runtime.BeginTick();
            Parallel.For(0, 16, _ =>
            {
                MethodProfileToken scope = _runtime.Enter(1, MethodProfileTag.None, null);
                Thread.SpinWait(1000);
                _runtime.Exit(scope);
            });
            _runtime.EndTick(tick);
            _runtime.TryGetLatestSnapshot(out MethodProfilerSnapshot? snapshot);

            MethodProfileSample[] samples = snapshot!.MethodSamples.Span.ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(samples.Sum(static sample => sample.Calls), Is.EqualTo(16));
                Assert.That(samples.All(static sample => sample.ThreadId > 0), Is.True);
                Assert.That(samples.All(static sample => sample.Inclusive > TimeSpan.Zero), Is.True);
            });
        }
    }

    private void Register(
        ulong methodId,
        MethodProfileTag tags = MethodProfileTag.None,
        string? requiredRuntimeInterface = null)
    {
        _runtime.RegisterMethod(new MethodProfileMetadata(
            methodId,
            "Test",
            "Test.Type",
            "Method" + methodId,
            "System.Void Method" + methodId + "()",
            tags,
            requiredRuntimeInterface));
    }
}
