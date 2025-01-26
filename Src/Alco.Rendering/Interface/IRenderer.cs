using Alco.Graphics;

namespace Alco.Rendering;

public interface IRenderer
{
    public void Begin(GPUFrameBuffer target);
    public void End();
}