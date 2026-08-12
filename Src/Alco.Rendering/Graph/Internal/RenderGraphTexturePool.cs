namespace Alco.Rendering;

/// <summary>
/// The texture store and assignment authority behind transient
/// <see cref="RenderGraphTexture"/> resources. The owning <see cref="RenderGraph"/>
/// computes lifetimes per frame and then performs a single allocation walk over the
/// used resources (sorted by first touch); this pool answers each slot request with
/// the following priority:
/// <list type="number">
/// <item>The slot's <b>sticky</b> entry (its assignment from last frame) when it was
/// released earlier in this walk — sticky reassignment keeps facades stable without
/// blocking reuse.</item>
/// <item>The <b>most recently released</b> entry of this walk — this is what makes
/// non-overlapping lifetimes alias the same texture.</item>
/// <item>The slot's sticky entry when still idle.</item>
/// <item>The <b>oldest idle</b> entry (deterministic fallback).</item>
/// <item>A newly materialized entry from the factory.</item>
/// </list>
/// For an unchanged schedule the walk is deterministic and reproduces last frame's
/// assignment exactly, so facades are never rebound and the path allocates no managed
/// memory.
/// <br/>The pool is handle-based (<see cref="object"/>) and created with a factory, so
/// the assignment logic is testable without a GPU device. Handles implementing
/// <see cref="System.IDisposable"/> are disposed by <see cref="Clear"/>.
/// </summary>
internal sealed class RenderGraphTexturePool : System.IDisposable
{
    private sealed class KeyState
    {
        /// <summary>Every entry materialized for this key (owns them).</summary>
        internal readonly List<object> All = new(2);

        /// <summary>Entries idle at the start of the current walk.</summary>
        internal readonly List<object> Idle = new(2);

        /// <summary>Entries released earlier in the current walk (reuse candidates).</summary>
        internal readonly List<object> Freed = new(2);
    }

    private readonly Dictionary<TexturePoolKey, KeyState> _states = new();
    private readonly System.Func<TexturePoolKey, string, object> _factory;

    /// <summary>
    /// Creates a pool. The factory is invoked on every cache miss to materialize a new
    /// entry for the given key; the name is a diagnostic label derived from the
    /// requesting resource.
    /// </summary>
    internal RenderGraphTexturePool(System.Func<TexturePoolKey, string, object> factory)
    {
        System.ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    /// <summary>The total number of materialized entries across all keys (diagnostics).</summary>
    internal int TotalCount
    {
        get
        {
            int count = 0;
            foreach (KeyState state in _states.Values)
            {
                count += state.All.Count;
            }
            return count;
        }
    }

    /// <summary>
    /// Resets the walk state: every materialized entry becomes idle and the released
    /// lists are emptied. Called once per frame before the allocation walk.
    /// </summary>
    internal void BeginFrame()
    {
        foreach (KeyState state in _states.Values)
        {
            state.Idle.Clear();
            state.Idle.AddRange(state.All);
            state.Freed.Clear();
        }
    }

    /// <summary>
    /// Allocates an entry for one resource slot, applying the priority order described
    /// in the class remarks.
    /// </summary>
    /// <param name="key">The slot's texture identity.</param>
    /// <param name="sticky">The entry the slot was assigned last frame, or null.</param>
    /// <param name="name">A diagnostic name for factory-created entries.</param>
    internal object Allocate(in TexturePoolKey key, object? sticky, string name)
    {
        if (!_states.TryGetValue(key, out KeyState? state))
        {
            state = new KeyState();
            _states.Add(key, state);
        }

        // Sticky reassignment from this walk's releases (keeps facades stable even
        // when the entry is shared with a resource whose lifetime ended earlier).
        if (sticky != null && state.Freed.Remove(sticky))
        {
            return sticky;
        }

        // Reuse the most recently released entry: non-overlapping lifetimes alias.
        if (state.Freed.Count > 0)
        {
            int last = state.Freed.Count - 1;
            object freed = state.Freed[last];
            state.Freed.RemoveAt(last);
            return freed;
        }

        // Sticky reassignment from the idle set.
        if (sticky != null && state.Idle.Remove(sticky))
        {
            return sticky;
        }

        // Oldest idle entry (deterministic fallback).
        if (state.Idle.Count > 0)
        {
            object idle = state.Idle[0];
            state.Idle.RemoveAt(0);
            return idle;
        }

        // Cache miss: materialize a new entry.
        object created = _factory(key, name);
        state.All.Add(created);
        return created;
    }

    /// <summary>
    /// Releases an entry whose owner's lifetime ended earlier in the current walk,
    /// making it the newest reuse candidate.
    /// </summary>
    internal void ReleaseExpired(in TexturePoolKey key, object entry)
    {
        if (!_states.TryGetValue(key, out KeyState? state))
        {
            state = new KeyState();
            _states.Add(key, state);
        }
        state.Freed.Add(entry);
    }

    /// <summary>
    /// Disposes every materialized entry (entries implementing
    /// <see cref="System.IDisposable"/>) and empties the pool. Must only be called
    /// between frames.
    /// </summary>
    internal void Clear()
    {
        foreach (KeyState state in _states.Values)
        {
            for (int i = 0; i < state.All.Count; i++)
            {
                if (state.All[i] is System.IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            state.All.Clear();
            state.Idle.Clear();
            state.Freed.Clear();
        }
        _states.Clear();
    }

    /// <summary>The number of materialized entries for a key (tests and diagnostics).</summary>
    internal int TotalCountFor(in TexturePoolKey key)
    {
        return _states.TryGetValue(key, out KeyState? state) ? state.All.Count : 0;
    }

    /// <summary>The number of idle entries for a key in the current walk (tests and diagnostics).</summary>
    internal int IdleCountFor(in TexturePoolKey key)
    {
        return _states.TryGetValue(key, out KeyState? state) ? state.Idle.Count : 0;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Clear();
    }
}
