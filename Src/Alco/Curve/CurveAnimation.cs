using System;
using System.Collections.Generic;

namespace Alco;

/// <summary>
/// Represents an animation based on a curve, capable of triggering events at specific times.
/// </summary>
/// <typeparam name="T">The type of the curve value (e.g., float, Vector2, Vector3).</typeparam>
public class CurveAnimation<T> where T : struct
{
    protected ICurve<T> valueCurve;
    protected PriorityList<CurveEvent> events;
    private float _lastT = 0;
    private bool _hasEventOnEnd = false;

    protected readonly Dictionary<string, Action> eventActions = new Dictionary<string, Action>();

    /// <summary>
    /// Gets the list of curve events.
    /// </summary>
    public IEnumerable<CurveEvent> Events => events;

    /// <summary>
    /// Gets the duration of the animation.
    /// </summary>
    public float Duration
    {
        get
        {
            if (valueCurve.Count == 0) return 0;
            return valueCurve[valueCurve.Count - 1].Time - valueCurve[0].Time;
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CurveAnimation{T}"/> class.
    /// </summary>
    /// <param name="valueCurve">The curve driving the animation values.</param>
    /// <param name="events">Optional list of events to trigger.</param>
    public CurveAnimation(ICurve<T> valueCurve, IReadOnlyList<CurveEvent>? events = null)
    {
        ArgumentNullException.ThrowIfNull(valueCurve);
        this.valueCurve = valueCurve;

        if (events == null)
        {
            this.events = new PriorityList<CurveEvent>();
        }
        else
        {
            this.events = new PriorityList<CurveEvent>(events, (a, b) => a.T.CompareTo(b.T));
        }
    }

    /// <summary>
    /// Evaluates the animation at the specified time, triggering any events within the elapsed range.
    /// </summary>
    /// <param name="t">The time to evaluate.</param>
    /// <returns>The interpolated value from the curve.</returns>
    public T Evaluate(float t)
    {
        T value = valueCurve.Evaluate(t);

        TryInvokeEventActionInRange(_lastT, t);

        _lastT = t;
        return value;
    }

    /// <summary>
    /// Tries to invoke a specific event by name.
    /// </summary>
    /// <param name="name">The name of the event.</param>
    /// <returns>True if the event was found and invoked; otherwise, false.</returns>
    public bool TryInvokeEventAction(string name)
    {
        if (eventActions.TryGetValue(name, out Action? @event))
        {
            @event();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Tries to invoke all events within the specified time range.
    /// </summary>
    /// <param name="start">The start time.</param>
    /// <param name="end">The end time.</param>
    /// <returns>True if any event was invoked.</returns>
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

        // Assuming AlgoBinarySearch works with PriorityList or IList
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

            if (curveEvent.T > end)
            {
                break;
            }

            if (curveEvent.IsFollowingDirection(direction) && TryInvokeEventAction(curveEvent.Name))
            {
                result = true;
            }

            if (curveEvent.T == end)
            {
                _hasEventOnEnd = true;
                break;
            }
        }

        return result;
    }

    /// <summary>
    /// Binds an action to an event name.
    /// </summary>
    /// <param name="name">The event name.</param>
    /// <param name="action">The action to execute.</param>
    public void BindEvent(string name, Action action)
    {
        if (eventActions.ContainsKey(name))
        {
            eventActions[name] += action;
            return;
        }

        eventActions.Add(name, action);
    }

    /// <summary>
    /// Unbinds an action from an event name.
    /// </summary>
    /// <param name="name">The event name.</param>
    /// <param name="action">The action to remove.</param>
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
