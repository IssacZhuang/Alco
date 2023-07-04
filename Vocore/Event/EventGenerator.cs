using System;
using System.Collections;
using System.Collections.Generic;

namespace Vocore
{
    public static class EventGenerator
    {
        private static int _index = 0;

        public static EventId Generate(string desc)
        {
            return new EventId(GetIndex(), desc);
        }

        private static int GetIndex()
        {
            return _index++;
        }
    }
}