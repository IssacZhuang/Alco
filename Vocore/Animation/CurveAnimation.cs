using System;
using System.Collections.Generic;

namespace Vocore
{
    public class CurveAnimation : ICurveAnimation
    {
        protected ICurve valueCurve;
        protected ICurve timeCurve;
        protected List<CurveEvent> events;
        private float _lastT = 0;

        protected readonly Dictionary<string, List<Action>> eventActions = new Dictionary<string, List<Action>>();

        public float Duration
        {
            get
            {
                if (timeCurve != null)
                {
                    return timeCurve.Points[timeCurve.Points.Count - 1].t - timeCurve.Points[0].t;
                }
                return valueCurve.Points[valueCurve.Points.Count - 1].t - valueCurve.Points[0].t;
            }
        }

        public CurveAnimation(ICurve valueCurve, ICurve timeCurve = null, IList<CurveEvent> events = null)
        {
            this.valueCurve = valueCurve;
            this.timeCurve = timeCurve;

            if (events == null)
            {
                this.events = new List<CurveEvent>();
            }
            else
            {
                this.events = new List<CurveEvent>(events);
            }
        }

        public CurveAnimation(ICurve valueCurve, IList<CurveEvent> events = null)
            : this(valueCurve, null, events)
        {
        }

        public float Evaluate(float t)
        {
            if (timeCurve != null)
            {
                t = timeCurve.Evaluate(t);
            }

            float value = valueCurve.Evaluate(t);



            _lastT = t;
            return value;
        }

        public bool TryInvokeEventAction(string name, float t)
        {
            List<Action> actions;
            bool result = false;
            if (eventActions.TryGetValue(name, out actions) && actions != null)
            {
                for (int i = 0; i < actions.Count; i++)
                {
                    actions[i]();
                    result = true;
                }
            }
            return result;
        }

        public bool TryGetEventAction(string name, out List<Action> actions)
        {
            return eventActions.TryGetValue(name, out actions);
        }

        public void BindEvent(string name, Action action)
        {
            List<Action> actions;
            if (!eventActions.TryGetValue(name, out actions))
            {
                actions.Add(action);
                return;
            }

            actions = new List<Action>();
            actions.Add(action);
            eventActions.Add(name, actions);
        }

        public void UnbindEvent(string name, Action action)
        {
            List<Action> actions;
            if (eventActions.TryGetValue(name, out actions))
            {
                actions.Remove(action);
            }
        }

        private int SearchFloorEvent(float t)
        {
            //binary search
            int index = -1;
            int left = 0;
            int right = events.Count - 1;
            while (left <= right)
            {
                int mid = (left + right) / 2;
                if (events[mid].t <= t)
                {
                    index = mid;
                    left = mid + 1;
                }
                else
                {
                    right = mid - 1;
                }
            }
            return index;
        }

        private int SearchCeilEvent(float t)
        {
            //binary search
            int index = -1;
            int left = 0;
            int right = events.Count - 1;
            while (left <= right)
            {
                int mid = (left + right) / 2;
                if (events[mid].t >= t)
                {
                    index = mid;
                    right = mid - 1;
                }
                else
                {
                    left = mid + 1;
                }
            }
            return index;
        }
    }
}

