using System;

namespace Vocore
{
    public static class Text_Event
    {
        public static string DuplicatedEvent(string id)
        {
            return $"EventTracker.Subscribe: event {id} already subscribed";
        }
    }
}