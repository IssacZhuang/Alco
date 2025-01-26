using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;

namespace Alco
{
    public static class EventGenerator
    {
        private static int _index = 0;

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