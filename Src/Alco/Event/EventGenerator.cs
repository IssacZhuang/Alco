using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;

namespace Alco
{
    /// <summary>
    /// Allocates unique event identifiers from a process-wide counter.
    /// </summary>
    public static class EventGenerator
    {
        private static int _index = 0;

        /// <summary>
        /// Creates a new event identifier with the given name.
        /// </summary>
        /// <param name="stringId">The human-readable event name.</param>
        /// <returns>The generated event identifier.</returns>
        public static EventId Generate(string stringId)
        {
            return new EventId(GetIndex(), stringId);
        }

        private static int GetIndex()
        {
            return Interlocked.Increment(ref _index);
        }
    }
}