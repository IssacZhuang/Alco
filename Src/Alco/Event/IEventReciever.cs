
namespace Alco
{
    public interface IEventReciever
    {
        void InvokeEvent(EventId evt);
        void InvokeEvent<TData>(EventId evt, TData data);
    }
}
