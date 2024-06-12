namespace Vocore.Graphics;

public abstract class GPUSwapcahin : BaseGPUObject
{
    public abstract GPUFrameBuffer FrameBuffer { get; }
    public abstract SurfaceSource SurfaceSource { get; }
    public abstract bool IsVSyncEnabled { get; set; }
    public abstract void Resize(uint width, uint height);
    public abstract void Present();
}