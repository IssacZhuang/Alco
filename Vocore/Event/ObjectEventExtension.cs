using System;
using System.Collections;
using System.Collections.Generic;

namespace Vocore
{
    public static class ObjectEventExtension
    {
        public static void RegisterEvent<TObject, TData>(TObject obj, Event evt, Action<TData> action) where TObject : class, IEventReciever
        {
            EventTracker<TObject, TData>.Subscribe(evt, obj, action);
        }

        public static void UnregisterEvent<TObject, TData>(TObject obj, Event evt) where TObject : class, IEventReciever
        {
            EventTracker<TObject, TData>.Unsubscribe(evt, obj);
        }

        public static void SendEvent<TObject, TData>(TObject obj, Event evt, TData data) where TObject : class, IEventReciever
        {
            EventTracker<TObject, TData>.SendEvent(evt, obj, data);
        }

        public static void RegisterEvent<TObject>(TObject obj, Event evt, Action action) where TObject : class, IEventReciever
        {
            EventTracker<TObject>.Subscribe(evt, obj, action);
        }

        public static void UnregisterEvent<TObject>(TObject obj, Event evt) where TObject : class, IEventReciever
        {
            EventTracker<TObject>.Unsubscribe(evt, obj);
        }

        public static void SendEvent<TObject>(TObject obj, Event evt) where TObject : class, IEventReciever
        {
            EventTracker<TObject>.SendEvent(evt, obj);
        }

    }
}