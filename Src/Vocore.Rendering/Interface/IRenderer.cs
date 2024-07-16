using Vocore.Graphics;

namespace Vocore.Rendering;

public interface IRenderer
{
    public void Begin(GPUFrameBuffer target);
    public void End();
}