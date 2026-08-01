namespace Alco.Graphics.NoGPU;

internal sealed class NoTimestampQuerySet : GPUTimestampQuerySet
{
    protected override GPUDevice Device => NoDevice.noDevice;

    /// <summary>Creates a no-op timestamp query set.</summary>
    /// <param name="count">The timestamp slot count.</param>
    /// <param name="name">The diagnostic name.</param>
    public NoTimestampQuerySet(uint count, string name) : base(count, name)
    {
    }

    protected override void Dispose(bool disposing)
    {
    }
}
