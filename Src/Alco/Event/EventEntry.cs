using System;
using System.Collections;

namespace Alco
{
    public struct EventEntry<T>
    {
        public EventId evt;
        public Action<T> action;
    }

    public struct EventEntry
    {
        public EventId evt;
        public Action action;
    }
}