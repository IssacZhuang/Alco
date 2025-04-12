using System.Threading.Tasks;
using NUnit.Framework;

namespace Alco.Engine.Test;


public class TestSynchronizationContext
{
    private class TestEngine : GameEngine
    {
        public TestEngine() : base(GameEngineSetting.CreateNoGPU())
        {
            //the GameSynchronizationContext will be installed after engine start
            Assert.That(SynchronizationContext.Current is GameSynchronizationContext, Is.False);
        }

        private Task _taskInvokeByMainThread;
        private Task _taskInvokeByTaskRun;

        private int _mainThreadId;

        protected override void OnStart()
        {
            Assert.That(SynchronizationContext.Current, Is.TypeOf<GameSynchronizationContext>());
            _mainThreadId = Environment.CurrentManagedThreadId;

            _taskInvokeByMainThread = TestInvokeByMainThread();

            _taskInvokeByTaskRun = Task.Run(TestInvokeByOtherThread);
        }

        protected override void OnUpdate(float delta)
        {
            base.OnUpdate(delta);
            if (_taskInvokeByMainThread.IsCompleted && _taskInvokeByTaskRun.IsCompleted)
            {
                TestContext.WriteLine("Test completed");
                Stop();
            }

        }

        private async Task TestInvokeByMainThread()
        {
            Assert.That(_mainThreadId, Is.EqualTo(Environment.CurrentManagedThreadId));
            await WaitAndCheckThreadId();
            Assert.That(_mainThreadId, Is.EqualTo(Environment.CurrentManagedThreadId));
            await Task.Run(() =>
            {
                Thread.Sleep(1000);
                Assert.That(_mainThreadId, Is.Not.EqualTo(Environment.CurrentManagedThreadId));
            });
            Assert.That(_mainThreadId, Is.EqualTo(Environment.CurrentManagedThreadId));
        }

        private async void TestInvokeByOtherThread()
        {
            Assert.That(_mainThreadId, Is.Not.EqualTo(Environment.CurrentManagedThreadId));
            await Task.Delay(1000);
            Assert.That(_mainThreadId, Is.Not.EqualTo(Environment.CurrentManagedThreadId));
        }

        private async Task WaitAndCheckThreadId()
        {
            await Task.Delay(1000);
            Assert.That(_mainThreadId, Is.EqualTo(Environment.CurrentManagedThreadId));
        }

    }

    [Test]
    public void Test()
    {
        var engine = new TestEngine();
        engine.Run();
    }
}

