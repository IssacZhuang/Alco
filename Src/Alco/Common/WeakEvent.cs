
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Alco;

public sealed class WeakEvent
{
    private readonly List<WeakReference> _listeners = new List<WeakReference>();
    private readonly ConditionalWeakTable<object, List<object>> _keepDelegateAlive = new ConditionalWeakTable<object, List<object>>();

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



public sealed class WeakEvent<TEventArgs> where TEventArgs : allows ref struct
{
    private readonly List<WeakReference> _listeners = new List<WeakReference>();
    private readonly ConditionalWeakTable<object, List<object>> _keepDelegateAlive = new ConditionalWeakTable<object, List<object>>();

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

    public void RemoveListener(Action<TEventArgs> handler)
    {
        _listeners.RemoveAll(wr => !wr.IsAlive || wr.Target == null || wr.Target.Equals(handler));
        object? target = handler.Target;
        if(target != null && _keepDelegateAlive.TryGetValue(target, out var list))
        {
            list.Remove(handler);
        }
    }

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

