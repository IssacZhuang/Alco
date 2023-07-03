using System;
using System.Collections;
using System.Runtime.CompilerServices;

namespace Vocore
{
    public struct Event : IEquatable<Event>
    {
        private const string DebugStringFormat = "Event: id = {0}, desc = {1}";
        private int _id;
        // used for debugging
        private string _desc;

        public Event(int id, string desc)
        {
            _id = id;
            _desc = desc;
        }

        public override string ToString()
        {
            return string.Format(DebugStringFormat, _id, _desc);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object obj)
        {
            return base.Equals(obj);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode()
        {
            return _id;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(Event other)
        {
            return _id == other._id;
        }

        // operator ==
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(Event lhs, Event rhs)
        {
            return lhs.Equals(rhs);
        }

        // operator !=
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(Event lhs, Event rhs)
        {
            return !lhs.Equals(rhs);
        }
    }
}