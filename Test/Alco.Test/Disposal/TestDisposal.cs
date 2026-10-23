using System.Threading.Tasks;

namespace Alco.Test;

/// <summary>Tests idempotent concurrent disposal.</summary>
public class TestDisposal
{
    private class TestDisposable : AutoDisposable
    {
        public volatile uint disposeCount = 0;

        protected override void Dispose(bool disposing)
        {
            disposeCount++;
        }
    }

    /// <summary>Tests that concurrent Dispose calls execute the disposal logic once.</summary>
    [Test]
    public void TestConcurrentDisposa()
    {
        TestDisposable disposable = new TestDisposable();
        Parallel.For(0, 1000, (i) =>
        {
            disposable.Dispose();
        });

        Assert.That(disposable.disposeCount, Is.EqualTo(1));
    }
}