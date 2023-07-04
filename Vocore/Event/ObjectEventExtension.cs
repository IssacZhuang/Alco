using System;
using System.Collections;
using System.Collections.Generic;

namespace Vocore
{
    public static class ObjectEventExtension
    {
        public static void RegisterEvent<TData>(this IEventReciever obj, EventId evt, Action<TData> action)
        {
            EventTracker<TData>.Subscribe(evt, obj, action);
        }

        public static void UnregisterEvent<TData>(this IEventReciever obj, EventId evt)
        {
            EventTracker<TData>.Unsubscribe(evt, obj);
        }

        public static void SendEvent<TData>(this IEventReciever obj, EventId evt, TData data)
        {
            EventTracker<TData>.SendEvent(evt, obj, data);
        }

        public static void RegisterEvent(this IEventReciever obj, EventId evt, Action action)
        {
            EventTracker.Subscribe(evt, obj, action);
        }

        public static void UnregisterEvent(this IEventReciever obj, EventId evt)
        {
            EventTracker.Unsubscribe(evt, obj);
        }

        public static void SendEvent(this IEventReciever obj, EventId evt)
        {
            EventTracker.SendEvent(evt, obj);
        }

    }
}