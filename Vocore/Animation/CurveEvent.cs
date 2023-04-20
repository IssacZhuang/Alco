using System;
using System.Collections.Generic;

namespace Vocore
{
    public struct CurveEvent : IComparable<CurveEvent>, ISortable
    {
        public float t;
        public string name;
        public TimeDirection direction;

        public CurveEvent(float t, string name, TimeDirection timeDirection = TimeDirection.Both)
        {
            this.t = t;
            this.name = name;
            this.direction = timeDirection;
        }

        public float SortKey => t;

        public bool IsFollowingDirection(TimeDirection direction)
        {
            return (this.direction & direction) != 0;
        }

        public int CompareTo(CurveEvent obj)
        {
            return t.CompareTo(obj.t);
        }
    }
}

