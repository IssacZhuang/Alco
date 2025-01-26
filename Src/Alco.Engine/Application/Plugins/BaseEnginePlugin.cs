using Alco.Rendering;

namespace Alco.Engine;

public abstract class BaseEnginePlugin : IEnginePlugin
{
    public virtual int Order => 0;

    public virtual void Dispose()
    {
        
    }

    public virtual void OnPostInitialize(GameEngine engine)
    {
        
    }
}