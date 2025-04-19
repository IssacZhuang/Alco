
namespace Alco.Engine;


public partial class GameEngine
{
    public void PostToMainThread(Action action)
    {
        PostToMainThread((state) => action(), null);
    }

    public virtual void PostToMainThread(SendOrPostCallback action, object? state)
    {
        ArgumentNullException.ThrowIfNull(action);
        _synchronizationContext.Post(action, state);
    }

    
}