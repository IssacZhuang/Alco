
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Alco;

/// <summary>
/// An event that holds its listeners through weak references so subscribers can be garbage collected.
/// </summary>
public sealed class WeakEvent
{
    private readonly List<WeakReference> _listeners = new List<WeakReference>();
    private readonly ConditionalWeakTable<object, List<object>> _keepDelegateAlive = new ConditionalWeakTable<object, List<object>>();

    /// <summary>
    /// Adds a handler that is invoked while its target is alive.
    /// </summary>
    /// <param name="handler">The handler to add.</param>
    public void AddListener(Action handler)
    {
        if (handler == null)
        {
            return;
        }
        _listeners.Add(new WeakReference(handler));
        if (handler.Target != null)
        {
            _keepDelegateAlive.GetOrCreateValue(handler.Target).Add(handler);
        }

    }

    /// <summary>
    /// Removes the specified handler and prunes dead listeners.
    /// </summary>
    /// <param name="handler">The handler to remove.</param>
    public void RemoveListener(Action handler)
    {
        if(handler == null)
        {
            return;
        }
        _listeners.RemoveAll(wr => !wr.IsAlive || wr.Target == null || wr.Target.Equals(handler));
        if(handler.Target != null && _keepDelegateAlive.TryGetValue(handler.Target, out var list))
        {
            list.Remove(handler);
        }
    }

    /// <summary>
    /// Invokes all live registered handlers.
    /// </summary>
    public void Invoke()
    {
        for (int i = _listeners.Count - 1; i >= 0; i--)
        {
            var weakReference = _listeners[i];
            if (weakReference.IsAlive)
            {
                (weakReference.Target as Action)?.Invoke();
            }
            else
            {
                _listeners.RemoveAt(i);
            }
        }
    }
}



/// <summary>
/// An event with arguments that holds its listeners through weak references so subscribers can be garbage collected.
/// </summary>
public sealed class WeakEvent<TEventArgs> where TEventArgs : allows ref struct
{
    private readonly List<WeakReference> _listeners = new List<WeakReference>();
    private readonly ConditionalWeakTable<object, List<object>> _keepDelegateAlive = new ConditionalWeakTable<object, List<object>>();

    /// <summary>
    /// Adds a handler that is invoked while its target is alive.
    /// </summary>
    /// <param name="handler">The handler to add.</param>
    public void AddListener(Action<TEventArgs> handler)
    {
        if(handler == null)
        {
            return;
        }
        _listeners.Add(new WeakReference(handler));
        if(handler.Target != null)
        {
            _keepDelegateAlive.GetOrCreateValue(handler.Target).Add(handler);
        }
    }

    /// <summary>
    /// Removes the specified handler and prunes dead listeners.
    /// </summary>
    /// <param name="handler">The handler to remove.</param>
    public void RemoveListener(Action<TEventArgs> handler)
    {
        _listeners.RemoveAll(wr => !wr.IsAlive || wr.Target == null || wr.Target.Equals(handler));
        object? target = handler.Target;
        if(target != null && _keepDelegateAlive.TryGetValue(target, out var list))
        {
            list.Remove(handler);
        }
    }

    /// <summary>
    /// Invokes all live registered handlers with the specified arguments.
    /// </summary>
    /// <param name="args">The event arguments.</param>
    public void Invoke(in TEventArgs args)
    {
        for (int i = _listeners.Count - 1; i >= 0; i--)
        {
            var weakReference = _listeners[i];
            if (weakReference.IsAlive)
            {
                (weakReference.Target as Action<TEventArgs>)?.Invoke(args);
            }
            else
            {
                _listeners.RemoveAt(i);
            }
        }
    }
}

