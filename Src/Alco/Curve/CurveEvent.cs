using System;
using System.Collections.Generic;

namespace Alco
{
    public struct CurveEvent : IComparable<CurveEvent>, ISortable
    {
        public float T;
        public string Name;
        public TimeDirection Direction;

        public CurveEvent(float t, string name, TimeDirection timeDirection = TimeDirection.Both)
        {
            this.T = t;
            this.Name = name;
            this.Direction = timeDirection;
        }

        public float SortKey => T;

        public bool IsFollowingDirection(TimeDirection direction)
        {
            return (this.Direction & direction) != 0;
        }

        public int CompareTo(CurveEvent obj)
        {
            return T.CompareTo(obj.T);
        }
    }
}

