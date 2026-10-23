using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Alco
{
    //[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    /// <summary>
    /// Handles an event carrying data of type <typeparamref name="TData"/>.
    /// </summary>
    /// <typeparam name="TData">The event data type.</typeparam>
    /// <param name="data">The event data.</param>
    public delegate void EventCallback<TData>(TData data);
    /// <summary>
    /// Handles an event that carries no data.
    /// </summary>
    public delegate void EventCallbackNoParam();

    /// <summary>
    /// Stores one callback per event identifier and dispatches events to it.
    /// </summary>
    public class EventTracker
    {
        private readonly Dictionary<EventId, object> _events = new Dictionary<EventId, object>();

        /// <summary>
        /// Initializes an empty event tracker.
        /// </summary>
        public EventTracker()
        {
        }

        /// <summary>
        /// Registers a callback for the specified event.
        /// </summary>
        /// <typeparam name="TData">The event data type.</typeparam>
        /// <param name="evt">The event identifier.</param>
        /// <param name="callback">The callback to invoke.</param>
        /// <returns>True if the callback was registered; false if the event already has a callback.</returns>
        public bool Subscribe<TData>(EventId evt, EventCallback<TData> callback)
        {
            if (_events.ContainsKey(evt))
            {
                Log.Error($"EventTracker.Subscribe: event {evt} already subscribed");
                return false;
            }

            _events.Add(evt, callback);
            return true;
        }

        /// <summary>
        /// Registers a parameterless callback for the specified event.
        /// </summary>
        /// <param name="evt">The event identifier.</param>
        /// <param name="callback">The callback to invoke.</param>
        /// <returns>True if the callback was registered; false if the event already has a callback.</returns>
        public bool Subscribe(EventId evt, EventCallbackNoParam callback)
        {
            if (_events.ContainsKey(evt))
            {
                return false;
            }

            _events.Add(evt, callback);
            return true;
        }

        /// <summary>
        /// Removes the callback registered for the specified event.
        /// </summary>
        /// <param name="evt">The event identifier.</param>
        /// <returns>True if a callback was removed; otherwise, false.</returns>
        public bool Unsubscribe(EventId evt)
        {
            if (!_events.ContainsKey(evt))
            {
                return false;
            }

            _events.Remove(evt);
            return true;
        }

        /// <summary>
        /// Invokes the callback registered for the specified event with the given data.
        /// </summary>
        /// <typeparam name="TData">The event data type.</typeparam>
        /// <param name="evt">The event identifier.</param>
        /// <param name="data">The event data.</param>
        /// <returns>True if a matching callback was invoked; otherwise, false.</returns>
        public bool Invoke<TData>(EventId evt, TData data)
        {
            if (!_events.TryGetValue(evt, out var callback))
            {
                return false;
            }

            if (callback is EventCallback<TData> cb)
            {
                cb(data);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Invokes the parameterless callback registered for the specified event.
        /// </summary>
        /// <param name="evt">The event identifier.</param>
        /// <returns>True if a matching callback was invoked; otherwise, false.</returns>
        public bool Invoke(EventId evt)
        {
            if (!_events.TryGetValue(evt, out var callback))
            {
                return false;
            }

            if (callback is EventCallbackNoParam cb)
            {
                cb();
                return true;
            }

            return false;
        }
    }
}