using System;
using System.Collections;
using System.Collections.Generic;

namespace Vocore
{
    public static class GlobalEventManger
    {
        public static void RegisterEvent<TData>(IEventReciever obj, EventId evt, Action<TData> action)
        {
            EventTracker<TData>.Subscribe(evt, obj, action);
        }

        public static void UnregisterEvent<TData>(IEventReciever obj, EventId evt)
        {
            EventTracker<TData>.Unsubscribe(evt, obj);
        }

        public static void InvkeEvent<TData>(IEventReciever obj, EventId evt, TData data)
        {
            EventTracker<TData>.InvokeEvent(evt, obj, data);
        }

        public static void RegisterEvent(IEventReciever obj, EventId evt, Action action)
        {
            EventTracker.Subscribe(evt, obj, action);
        }

        public static void UnregisterEvent(IEventReciever obj, EventId evt)
        {
            EventTracker.Unsubscribe(evt, obj);
        }

        public static void InvokeEvent(IEventReciever obj, EventId evt)
        {
            EventTracker.InvokeEvent(evt, obj);
        }

    }
}