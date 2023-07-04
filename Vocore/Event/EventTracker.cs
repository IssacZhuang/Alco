using System;
using System.Collections;
using System.Collections.Generic;

namespace Vocore
{
    internal static class EventTracker<TData>
    {
        private static Dictionary<object, List<EventEntry<TData>>> _events = new Dictionary<object, List<EventEntry<TData>>>();

        public static void Subscribe(EventId evt, object target, Action<TData> action)
        {
            if (!_events.ContainsKey(target))
            {
                _events.Add(target, new List<EventEntry<TData>>());
            }

            _events[target].Add(new EventEntry<TData> { evt = evt, action = action });
        }

        public static void Unsubscribe(EventId evt, object target)
        {
            if (!_events.TryGetValue(target, out var list))
            {
                return;
            }

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].evt == evt)
                {
                    list.RemoveAt(i);
                    return;
                }
            }

            if (list.Count == 0)
            {
                _events.Remove(target);
            }
        }

        public static void Unsubscribe(object target)
        {
            _events.Remove(target);
        }

        public static void SendEvent(EventId evt, object target, TData data)
        {
            if (!_events.TryGetValue(target, out var list))
            {
                return;
            }

            for (int i = 0; i < list.Count; i++)
            {

                if (list[i].evt == evt)
                {
                    list[i].action(data);
                    break;
                }
            }
        }

        public static void Clear()
        {
            _events.Clear();
        }
    }

    internal static class EventTracker
    {
        private static Dictionary<object, List<EventEntry>> _events = new Dictionary<object, List<EventEntry>>();

        public static void Subscribe(EventId evt, object target, Action action)
        {
            if (!_events.ContainsKey(target))
            {
                _events.Add(target, new List<EventEntry>());
            }

            _events[target].Add(new EventEntry { evt = evt, action = action });
        }

        public static void Unsubscribe(EventId evt, object target)
        {
            if (!_events.TryGetValue(target, out var list))
            {
                return;
            }

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].evt == evt)
                {
                    list.RemoveAt(i);
                }
            }

            if (list.Count == 0)
            {
                _events.Remove(target);
            }
        }

        public static void Unsubscribe(object target)
        {
            _events.Remove(target);
        }

        public static void SendEvent(EventId evt, object target)
        {
            if (!_events.TryGetValue(target, out var list))
            {
                return;
            }

            for (int i = 1; i < list.Count; i++)
            {
                if (list[i].evt == evt)
                {
                    list[i].action();
                    break;
                }
            }
        }

        public static void Clear()
        {
            _events.Clear();
        }
    }
}