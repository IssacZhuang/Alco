using System;
using System.Collections.Generic;
using System.Threading;

namespace Vocore.Test;

public class TestTask
{
    private class TestAddTask : ReusableTask<int>
    {
        public int value;
        protected override int ExecuteCore()
        {
            return ++value;
        }
    }

    [Test(Description = "Test Task")]
    public void TestReuseableTask()
    {
        TestAddTask task = new TestAddTask();
        task.Run();
        Assert.That(task.Result, Is.EqualTo(1));
        task.Run();
        Assert.That(task.Result, Is.EqualTo(2));
        task.Run();
        Assert.That(task.Result, Is.EqualTo(3));

        for (int i = 0; i < 10000; i++)
        {
            task.Run();
            task.Wait();
        }

        Assert.That(task.Result, Is.EqualTo(10003));
    }
}
