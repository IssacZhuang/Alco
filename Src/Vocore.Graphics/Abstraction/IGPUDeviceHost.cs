namespace Vocore.Graphics;

public interface IGPUDeviceHost
{
    event Action OnEndFrame;
    event Action OnDispose;
}