using System;
using System.Collections;
using System.Runtime.CompilerServices;

namespace Alco
{
    /// <summary>
    /// Identifies an event by an integer id and a debugging description.
    /// </summary>
    public struct EventId : IEquatable<EventId>
    {
        private const string DebugStringFormat = "Event: id = {0}, stringId = {1}";
        private int _id;
        // used for debugging
        private string _stringId;

        /// <summary>
        /// Initializes an event identifier.
        /// </summary>
        /// <param name="id">The numeric event id.</param>
        /// <param name="desc">The description used for debugging.</param>
        public EventId(int id, string desc)
        {
            _id = id;
            _stringId = desc;
        }

        /// <summary>
        /// Returns a debug string containing the id and description.
        /// </summary>
        /// <returns>A formatted event identifier.</returns>
        public override string ToString()
        {
            return string.Format(DebugStringFormat, _id, _stringId);
        }

        /// <summary>
        /// Determines whether the specified object is an event identifier with the same id and description.
        /// </summary>
        /// <param name="obj">The object to compare.</param>
        /// <returns>True if the object equals this identifier by all fields.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object? obj)
        {
            return base.Equals(obj);
        }


        /// <summary>
        /// Returns the event id as the hash code.
        /// </summary>
        /// <returns>The numeric event id.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode()
        {
            return _id;
        }

        /// <summary>
        /// Determines whether this identifier has the same id as another.
        /// </summary>
        /// <param name="other">The identifier to compare.</param>
        /// <returns>True if the ids are equal.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(EventId other)
        {
            return _id == other._id;
        }

        // operator ==
        /// <summary>
        /// Determines whether two event identifiers have the same id.
        /// </summary>
        /// <param name="lhs">The first identifier.</param>
        /// <param name="rhs">The second identifier.</param>
        /// <returns>True if the ids are equal.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(EventId lhs, EventId rhs)
        {
            return lhs.Equals(rhs);
        }

        // operator !=
        /// <summary>
        /// Determines whether two event identifiers have different ids.
        /// </summary>
        /// <param name="lhs">The first identifier.</param>
        /// <param name="rhs">The second identifier.</param>
        /// <returns>True if the ids differ.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(EventId lhs, EventId rhs)
        {
            return !lhs.Equals(rhs);
        }
    }
}