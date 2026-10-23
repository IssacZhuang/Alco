
namespace Alco
{
    /// <summary>
    /// Receives events dispatched by an event system.
    /// </summary>
    public interface IEventReciever
    {
        void InvokeEvent(EventId evt);
        void InvokeEvent<TData>(EventId evt, TData data);
    }
}
