
namespace Vocore.Engine;

public abstract class BaseEngineSystem : IEngineSystem
{
    public virtual int Order => 0;

    

    public virtual void OnResize(int2 size)
    {
        
    }

    public virtual void OnStart()
    {
        
    }

    public virtual void OnStop()
    {
        
    }

    public virtual void OnTick(float delta)
    {
        
    }

    public virtual void OnPostTick(float delta)
    {

    }

    public virtual void OnUpdate(float delta)
    {
        
    }
    

    public virtual void OnPostUpdate(float delta)
    {

    }

    public virtual void OnPreSwapFrame()
    {

    }

    public virtual void Dispose()
    {

    }

    
}