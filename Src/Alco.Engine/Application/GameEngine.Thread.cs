
namespace Alco.Engine;


public partial class GameEngine
{
    public void InvokeOnMainThread(SendOrPostCallback action, object? state = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        _synchronizationContext.Post(action, state);
    }
}