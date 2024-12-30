namespace Vocore.Graphics;

public interface IGPULifeCycleProvider
{
    event Action OnEndFrame;
    event Action OnDispose;
}