using System;

namespace Vocore;

/// <summary>
/// This class is used to schedule actions to be executed at a specific interval.
/// </summary>
public class Scheduler
{
    private struct Context
    {
        public uint Interval;
        public uint Loop;
        public uint Current;
        public Action Action;
    }

    private readonly UnorderedList<Context> _contexts = new();
    
    public event Action<Exception>? OnError;

    /// <summary>
    /// Schedules an action to be executed after a delay.
    /// </summary>
    /// <param name="delay">The delay in frames before the action is executed.</param>
    /// <param name="action">The action to be executed.</param>
    public void Schedule(uint delay, Action action)
    {
        _contexts.Add(new Context { Interval = delay, Loop = 1, Current = 0, Action = action });
    }

    /// <summary>
    /// Schedules an action to be executed at a specific interval.
    /// </summary>
    /// <param name="interval">The interval in frames at which the action is executed.</param>
    /// <param name="loop">The number of times the action is executed. If set to 0, the action is executed indefinitely.</param>
    /// <param name="action">The action to be executed.</param>
    public void Schedule(uint interval, uint loop, Action action)
    {
        _contexts.Add(new Context { Interval = interval, Loop = loop, Current = 0, Action = action });
    }

    /// <summary>
    /// Updates the scheduler.
    /// </summary>
    public void Update()
    {
        for (var i = 0; i < _contexts.Count; i++)
        {
            var context = _contexts[i];
            context.Current++;

            if (context.Current >= context.Interval)
            {
                try
                {
                    context.Action();
                }
                catch (Exception e)
                {
                    OnError?.Invoke(e);
                }
                context.Current = 0;
                
                if (context.Loop > 0)
                {
                    context.Loop--;
                    if (context.Loop == 0)
                    {
                        _contexts.RemoveAt(i);
                        i--;
                        continue;
                    }
                }
            }

            _contexts[i] = context;

        }
    }

    public void Unschedule(Action action)
    {
        int length = _contexts.Count;
        for (var i = 0; i < length; i++)
        {
            if (_contexts[i].Action == action)
            {
                _contexts[i] = _contexts.RemoveLast();  
                length--;
                i--;
            }
        }
    }

    public void UnscheduleAll()
    {
        _contexts.Clear();
    }
}
