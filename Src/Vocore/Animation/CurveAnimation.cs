using System;
using System.Collections.Generic;

namespace Vocore
{
    public class CurveAnimation : ICurveAnimation
    {
        protected ICurve valueCurve;
        protected PriorityList<CurveEvent> events;
        private float _lastT = 0;
        private bool _hasEventOnEnd = false;

        protected readonly Dictionary<string, Action> eventActions = new Dictionary<string, Action>();

        public IEnumerable<CurveEvent> Events => events;

        public float Duration
        {
            get
            {
                return valueCurve.Points[valueCurve.Points.Count - 1].t - valueCurve.Points[0].t;
            }
        }

        public CurveAnimation(ICurve valueCurve, IReadOnlyList<CurveEvent>? events = null)
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
            if (eventActions.TryGetValue(name, out Action? @event))
            {
                @event();
                return true;
            }
            return false;
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

            int index = AlgoBinarySearch.BinarySearchCeil(events, start);
            if (index < 0)
            {
                return false;
            }

            bool result = false;
            CurveEvent curveEvent;
            for (int i = index; i < events.Count; i++)
            {
                if (_hasEventOnEnd)
                {
                    _hasEventOnEnd = false;
                    continue;
                }

                curveEvent = events[i];

                if (curveEvent.t > end)
                {
                    break;
                }

                if (curveEvent.IsFollowingDirection(direction) && TryInvokeEventAction(curveEvent.name))
                {
                    result = true;
                }

                if (curveEvent.t == end)
                {
                    _hasEventOnEnd = true;
                    break;
                }
            }

            return result;
        }

        public void BindEvent(string name, Action action)
        {
            if (eventActions.ContainsKey(name))
            {
                eventActions[name] += action;
                return;
            }

            eventActions.Add(name, action);
        }

        public void UnbindEvent(string name, Action action)
        {
            if (eventActions.ContainsKey(name))
            {
#pragma warning disable CS8601
                eventActions[name] -= action;
#pragma warning restore CS8601
                return;
            }
        }
    }
}

