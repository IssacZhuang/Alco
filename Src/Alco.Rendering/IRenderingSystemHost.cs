namespace Alco.Rendering;

public interface IRenderingSystemHost
{
    event Action<float> OnUpdate;
    event Action OnDispose;
}

