using System;
using System.Collections.Generic;

namespace Alco
{
    /// <summary>
    /// A named event at a time on a curve, optionally restricted to a time direction.
    /// </summary>
    public struct CurveEvent : IComparable<CurveEvent>, ISortable
    {
        public float T;
        public string Name;
        public TimeDirection Direction;

        /// <summary>
        /// Initializes a curve event at the given time and name.
        /// </summary>
        /// <param name="t">The time of the event.</param>
        /// <param name="name">The event name.</param>
        /// <param name="timeDirection">The time directions in which the event plays.</param>
        public CurveEvent(float t, string name, TimeDirection timeDirection = TimeDirection.Both)
        {
            this.T = t;
            this.Name = name;
            this.Direction = timeDirection;
        }

        /// <summary>
        /// Gets the sort key, which is the event time.
        /// </summary>
        public float SortKey => T;

        /// <summary>
        /// Determines whether the event plays when time runs in the given direction.
        /// </summary>
        /// <param name="direction">The time direction to test.</param>
        /// <returns>True when the event is enabled for the direction.</returns>
        public bool IsFollowingDirection(TimeDirection direction)
        {
            return (this.Direction & direction) != 0;
        }

        /// <summary>
        /// Compares events by time.
        /// </summary>
        /// <param name="obj">The event to compare against.</param>
        /// <returns>A negative value, zero or a positive value according to the time order.</returns>
        public int CompareTo(CurveEvent obj)
        {
            return T.CompareTo(obj.T);
        }
    }
}

