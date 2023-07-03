using System;
using System.Collections;

namespace Vocore
{
    public struct EventEntry<T>
    {
        public Event evt;
        public Action<T> action;
    }

    public struct EventEntry
    {
        public Event evt;
        public Action action;
    }
}