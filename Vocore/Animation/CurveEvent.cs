using System;
using System.Collections.Generic;

namespace Vocore
{
    public struct CurveEvent
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
    }
}

