namespace Alco.Graphics;

internal struct AtomicSpinLock
{
    private int _lock;

    public void Lock()
    {
        while (Interlocked.CompareExchange(ref _lock, 1, 0) != 0)
        {
            Thread.Yield();
        }
    }

    public void Unlock()
    {
        Interlocked.Exchange(ref _lock, 0);
    }
}