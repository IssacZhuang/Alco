using System;
using System.Collections.Generic;

namespace Vocore
{
    public class CurveAnimation : ICurveAnimation
    {
        protected ICurve valueCurve;
        protected PriorityList<CurveEvent> events;
        private float _lastT = 0;

        public IEnumerable<CurveEvent> Events => events;

        protected readonly Dictionary<string, List<Action>> eventActions = new Dictionary<string, List<Action>>();

        public float Duration
        {
            get
            {
                return valueCurve.Points[valueCurve.Points.Count - 1].t - valueCurve.Points[0].t;
            }
        }

        public CurveAnimation(ICurve valueCurve, IList<CurveEvent> events = null)
        {
            this.valueCurve = valueCurve;

            if (events == null)
            {
                this.events = new PriorityList<CurveEvent>();
            }
            else
            {
                this.events = new PriorityList<CurveEvent>(events, (a, b) => a.t.CompareTo(b.t));
            }
        }

        public float Evaluate(float t)
        {
            float value = valueCurve.Evaluate(t);

            TryInvokeEventActionInRange(_lastT, t);

            _lastT = t;
            return value;
        }

        public bool TryInvokeEventAction(string name)
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

        public bool TryInvokeEventActionInRange(float start, float end)
        {
            TimeDirection direction = TimeDirection.Clockwise;
            if (start > end)
            {
                float tmp = start;
                start = end;
                end = tmp;
                direction = TimeDirection.CounterClockwise;
            }

            int index = UtilsAlgorithm.BinarySearchCeil(events, start);
            if (index < 0)
            {
                return false;
            }

            bool result = false;
            for (int i = index; i < events.Count; i++)
            {
                if (events[i].t > end)
                {
                    break;
                }

                if (events[i].IsFollowingDirection(direction) && TryInvokeEventAction(events[i].name))
                {
                    result = true;
                }
            }

            return result;
        }

        public void BindEvent(string name, Action action)
        {
            List<Action> actions;
            if (eventActions.TryGetValue(name, out actions))
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

        private bool TryGetEventAction(string name, out List<Action> actions)
        {
            return eventActions.TryGetValue(name, out actions);
        }
    }
}

