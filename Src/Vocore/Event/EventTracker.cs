using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Vocore
{
    //[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void EventCallback<TData>(TData data);
    public delegate void EventCallbackNoParam();

    public class EventTracker
    {
        private readonly Dictionary<EventId, object> _events = new Dictionary<EventId, object>();

        public EventTracker()
        {
        }

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

        public bool Subscribe(EventId evt, EventCallbackNoParam callback)
        {
            if (_events.ContainsKey(evt))
            {
                return false;
            }

            _events.Add(evt, callback);
            return true;
        }

        public bool Unsubscribe(EventId evt)
        {
            if (!_events.ContainsKey(evt))
            {
                return false;
            }

            _events.Remove(evt);
            return true;
        }

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