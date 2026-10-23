using System;
using System.Collections;

namespace Alco
{
    /// <summary>
    /// Associates an event identifier with a typed action.
    /// </summary>
    /// <typeparam name="T">The event argument type.</typeparam>
    public struct EventEntry<T>
    {
        public EventId evt;
        public Action<T> action;
    }

    /// <summary>
    /// Associates an event identifier with an action.
    /// </summary>
    public struct EventEntry
    {
        public EventId evt;
        public Action action;
    }
}