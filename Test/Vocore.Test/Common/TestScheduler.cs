using System;
using NUnit.Framework;

namespace Vocore.Test;

public class TestScheduler
{
    private Scheduler _scheduler = null!;

    [SetUp]
    public void Setup()
    {
        _scheduler = new Scheduler();
    }

    [Test]
    public void TestSingleSchedule()
    {
        var executed = false;
        _scheduler.Schedule(5, () => executed = true);

        // Should not execute before interval
        for (var i = 0; i < 4; i++)
        {
            _scheduler.Update();
            Assert.That(executed, Is.False);
        }

        // Should execute at interval
        _scheduler.Update();
        Assert.That(executed, Is.True);
    }

    [Test]
    public void TestRepeatedSchedule()
    {
        var count = 0;
        _scheduler.Schedule(2, 3, () => count++);

        // Should execute 3 times
        for (var i = 0; i < 10; i++)
        {
            _scheduler.Update();
        }

        Assert.That(count, Is.EqualTo(3));
    }

    [Test]
    public void TestInfiniteLoop()
    {
        var count = 0;
        _scheduler.Schedule(2, 0, () => count++);

        // Should execute 5 times with interval of 2
        for (var i = 0; i < 10; i++)
        {
            _scheduler.Update();
        }

        Assert.That(count, Is.EqualTo(5));
    }

    [Test]
    public void TestUnschedule()
    {
        var count = 0;
        Action action = () => count++;

        _scheduler.Schedule(2, 0, action);

        // Execute twice
        for (var i = 0; i < 4; i++)
        {
            _scheduler.Update();
        }

        _scheduler.Unschedule(action);

        // Should not execute after unscheduling
        for (var i = 0; i < 4; i++)
        {
            _scheduler.Update();
        }

        Assert.That(count, Is.EqualTo(2));
    }

    [Test]
    public void TestUnscheduleAll()
    {
        var count1 = 0;
        var count2 = 0;

        _scheduler.Schedule(2, 0, () => count1++);
        _scheduler.Schedule(3, 0, () => count2++);

        // Execute a few times
        for (var i = 0; i < 6; i++)
        {
            _scheduler.Update();
        }

        _scheduler.UnscheduleAll();

        // Should not execute after unscheduling all
        for (var i = 0; i < 6; i++)
        {
            _scheduler.Update();
        }

        Assert.That(count1, Is.EqualTo(3));
        Assert.That(count2, Is.EqualTo(2));
    }

    [Test]
    public void TestErrorHandling()
    {
        Exception caughtException = null;
        _scheduler.OnError += e => caughtException = e;

        _scheduler.Schedule(1, () => throw new InvalidOperationException("Test exception"));

        _scheduler.Update();

        Assert.That(caughtException, Is.Not.Null);
        Assert.That(caughtException, Is.TypeOf<InvalidOperationException>());
        Assert.That(caughtException!.Message, Is.EqualTo("Test exception"));
    }
}