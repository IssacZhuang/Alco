using System.Threading.Tasks;

namespace Vocore.Test;

public class Test_Disposal
{
    private class TestDisposable : AutoDisposable
    {
        public volatile uint disposeCount = 0;

        protected override void Dispose(bool disposing)
        {
            disposeCount++;
        }
    }

    [Test]
    public void Test_ConcurrentDisposa()
    {
        TestDisposable disposable = new TestDisposable();
        Parallel.For(0, 1000, (i) =>
        {
            disposable.Dispose();
        });

        Assert.That(disposable.disposeCount, Is.EqualTo(1));
    }
}