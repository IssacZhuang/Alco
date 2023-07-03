using System;
using System.Collections;
using System.Collections.Generic;

namespace Vocore
{
    internal static class EventTracker<TObject, TData>
    {
        private static Dictionary<TObject, List<EventEntry<TData>>> _events = new Dictionary<TObject, List<EventEntry<TData>>>();

        public static void Subscribe(Event evt, TObject target, Action<TData> action)
        {
            if (!_events.ContainsKey(target))
            {
                _events.Add(target, new List<EventEntry<TData>>());
            }

            _events[target].Add(new EventEntry<TData> { evt = evt, action = action });
        }

        public static void Unsubscribe(Event evt, TObject target)
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

        public static void Unsubscribe(TObject target)
        {
            _events.Remove(target);
        }

        public static void SendEvent(Event evt, TObject target, TData data)
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

    internal static class EventTracker<TObject>
    {
        private static Dictionary<TObject, List<EventEntry>> _events = new Dictionary<TObject, List<EventEntry>>();

        public static void Subscribe(Event evt, TObject target, Action action)
        {
            if (!_events.ContainsKey(target))
            {
                _events.Add(target, new List<EventEntry>());
            }

            _events[target].Add(new EventEntry { evt = evt, action = action });
        }

        public static void Unsubscribe(Event evt, TObject target)
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

        public static void Unsubscribe(TObject target)
        {
            _events.Remove(target);
        }

        public static void SendEvent(Event evt, TObject target)
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