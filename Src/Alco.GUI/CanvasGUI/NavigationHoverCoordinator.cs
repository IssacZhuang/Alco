namespace Alco.GUI;

/// <summary>
/// The four cardinal navigation directions.
/// </summary>
public enum NavDirection
{
    Up,
    Down,
    Left,
    Right,
}

/// <summary>
/// How directional input maps to navigation movement within a list.
/// </summary>
public enum NavOrientation
{
    /// <summary>Up/Down move; Left/Right ignored.</summary>
    Vertical,

    /// <summary>Left/Right move; Up/Down ignored.</summary>
    Horizontal,

    /// <summary>All four directions move (grid): Up/Down by row, Left/Right by column.</summary>
    Grid,
}

/// <summary>
/// Coordinates D-Pad/arrow-key navigation and canvas hover for any navigable list.
/// Owns the edge detection, focus tracking, hover application, and hover-seeded
/// start logic.
/// </summary>
/// <remarks>
/// The host control supplies two callbacks:
/// <list type="bullet">
/// <item><see cref="ResolveNode"/> maps a focused index to its visible node.</item>
/// <item><see cref="TryNavigate"/> applies one step in a direction from an index,
///   returning the new index or null when blocked (e.g. clamped at an edge,
///   or skipped past non-navigable items).</item>
/// </list>
/// Direction-to-axis mapping is controlled by <see cref="Orientation"/>; the
/// per-host navigation math lives in <see cref="TryNavigate"/>.
/// </remarks>
public sealed class NavigationHoverCoordinator
{
    private readonly INavigationFocusable _owner;

    private int _focusedIndex = -1;
    private bool _canNavigate = true;
    private bool _focusChanged;

    private bool _prevUp;
    private bool _prevDown;
    private bool _prevLeft;
    private bool _prevRight;

    /// <summary>
    /// Creates a coordinator owned by <paramref name="owner"/>.
    /// </summary>
    /// <param name="owner">The control whose <see cref="INavigationFocusable"/>
    /// identity is compared against the canvas <see cref="INavigationContext.NavigationFocus"/>
    /// to decide whether this coordinator processes input this frame.</param>
    public NavigationHoverCoordinator(INavigationFocusable owner)
    {
        _owner = owner;
    }

    /// <summary>
    /// Resolves a focused index to its currently visible node, or null.
    /// Set by the host.
    /// </summary>
    public Func<int, UINode?>? ResolveNode { get; set; }

    /// <summary>
    /// Attempts to move focus one step in <paramref name="direction"/> starting
    /// from <paramref name="fromIndex"/>. Returns the new focused index, or null
    /// if the move is blocked (no valid target in that direction).
    /// Set by the host.
    /// </summary>
    public Func<NavDirection, int, int?>? TryNavigate { get; set; }

    /// <summary>
    /// Maps directional input to navigation axes. Defaults to vertical.
    /// </summary>
    public NavOrientation Orientation { get; set; } = NavOrientation.Vertical;

    /// <summary>
    /// Optional callback invoked after a successful navigation (e.g. a virtual
    /// list scrolling the focused item into view).
    /// </summary>
    public Action<INavigationContext, int>? OnNavigated { get; set; }

    /// <summary>
    /// Optional callback that, given a hovered node, returns the focused index
    /// of the navigable child it belongs to (walking up the parent chain), or
    /// null when the hover is outside this list. Used for hover-seeded navigation
    /// start. When unset, hover-seeding is disabled and navigation falls back to
    /// <see cref="FocusedIndex"/> / first / last.
    /// </summary>
    public Func<UINode?, int?>? IndexOfHoveredChild { get; set; }

    /// <summary>
    /// Gets or sets whether this coordinator can process navigation input.
    /// </summary>
    public bool CanNavigate
    {
        get => _canNavigate;
        set => _canNavigate = value;
    }

    /// <summary>
    /// Gets the current focused index, or -1 when nothing is focused.
    /// </summary>
    public int FocusedIndex => _focusedIndex;

    /// <summary>
    /// Gets the currently focused node via <see cref="ResolveNode"/>, or null.
    /// </summary>
    public UINode? FocusedNode => ResolveNode?.Invoke(_focusedIndex);

    /// <summary>
    /// Sets focus to the given index. Pass -1 to clear. The next tick re-applies hover.
    /// </summary>
    /// <param name="index">The index to focus, or -1 to clear.</param>
    public void SetFocus(int index)
    {
        if (index < 0)
        {
            _focusedIndex = -1;
            return;
        }
        _focusedIndex = index;
        _focusChanged = true;
    }

    /// <summary>
    /// Clears the current focus.
    /// </summary>
    public void ClearFocus()
    {
        _focusedIndex = -1;
    }

    /// <summary>
    /// Runs one frame of navigation processing against <paramref name="ctx"/>.
    /// Call from the host's tick each frame.
    /// </summary>
    /// <param name="ctx">The navigation context (usually the canvas).</param>
    public void Tick(INavigationContext ctx)
    {
        if (!_canNavigate)
        {
            return;
        }

        if (_focusChanged)
        {
            _focusChanged = false;
            if (!IsHoverOnNavigableChild(ctx.Hovered))
            {
                ApplyHover(ctx);
            }
        }

        if (ctx.NavigationFocus != _owner)
        {
            SyncEdgeState(ctx.InputTracker);
            _focusedIndex = -1;
            return;
        }

        IUIInputTracker inputTracker = ctx.InputTracker;

        bool up = inputTracker.IsKeyUpPressing;
        bool down = inputTracker.IsKeyDownPressing;
        bool left = inputTracker.IsKeyLeftPressing;
        bool right = inputTracker.IsKeyRightPressing;

        bool upEdge = up && !_prevUp;
        bool downEdge = down && !_prevDown;
        bool leftEdge = left && !_prevLeft;
        bool rightEdge = right && !_prevRight;

        _prevUp = up;
        _prevDown = down;
        _prevLeft = left;
        _prevRight = right;

        NavDirection? direction = ResolveDirection(upEdge, downEdge, leftEdge, rightEdge);
        if (direction == null)
        {
            return;
        }

        // Hover-seeded start: when the cursor is over a navigable child of this list,
        // begin navigation from it. This makes the cursor the source of truth for the
        // "current" position, matching the user's expectation that navigation continues
        // from where they're hovering. Falls back to the focused index when the cursor
        // is outside the list, and to the list edge when nothing is focused.
        int fromIndex = _focusedIndex;
        if (IndexOfHoveredChild != null)
        {
            int? hovered = IndexOfHoveredChild(ctx.Hovered);
            if (hovered.HasValue)
            {
                fromIndex = hovered.Value;
            }
        }

        int? moved = TryNavigate?.Invoke(direction.Value, fromIndex);
        if (moved.HasValue)
        {
            _focusedIndex = moved.Value;
            ApplyHover(ctx);
            OnNavigated?.Invoke(ctx, moved.Value);
        }
        else if (ShouldRestoreFocusedHover(ctx))
        {
            ApplyHover(ctx);
        }
    }

    /// <summary>
    /// Returns true if <paramref name="hovered"/> is <paramref name="target"/>
    /// or one of its descendants in the node tree.
    /// </summary>
    /// <param name="hovered">The hovered node.</param>
    /// <param name="target">The candidate ancestor.</param>
    /// <returns>True when the hover is within the target's subtree.</returns>
    public static bool IsHoveredWithin(UINode? hovered, UINode target)
    {
        UINode? node = hovered;
        while (node != null)
        {
            if (ReferenceEquals(node, target))
            {
                return true;
            }
            node = node.Parent;
        }
        return false;
    }

    private NavDirection? ResolveDirection(bool upEdge, bool downEdge, bool leftEdge, bool rightEdge)
    {
        switch (Orientation)
        {
            case NavOrientation.Vertical:
                if (upEdge) return NavDirection.Up;
                if (downEdge) return NavDirection.Down;
                return null;
            case NavOrientation.Horizontal:
                if (leftEdge) return NavDirection.Left;
                if (rightEdge) return NavDirection.Right;
                return null;
            case NavOrientation.Grid:
                if (upEdge) return NavDirection.Up;
                if (downEdge) return NavDirection.Down;
                if (leftEdge) return NavDirection.Left;
                if (rightEdge) return NavDirection.Right;
                return null;
            default:
                return null;
        }
    }

    private void ApplyHover(INavigationContext ctx)
    {
        UINode? focused = FocusedNode;
        if (focused != null)
        {
            ctx.SetHovered(focused);
        }
    }

    private bool IsHoverOnNavigableChild(UINode? hovered)
    {
        return IndexOfHoveredChild?.Invoke(hovered).HasValue ?? false;
    }

    private bool ShouldRestoreFocusedHover(INavigationContext ctx)
    {
        UINode? focused = FocusedNode;
        return focused != null
            && focused.IsEnable
            && !IsHoveredWithin(ctx.Hovered, focused);
    }

    /// <summary>
    /// Updates edge-detection state without triggering navigation, so that becoming
    /// the active navigator on a later frame does not fire a stale-edge burst.
    /// </summary>
    /// <param name="inputTracker">The input tracker to sample.</param>
    private void SyncEdgeState(IUIInputTracker inputTracker)
    {
        _prevUp = inputTracker.IsKeyUpPressing;
        _prevDown = inputTracker.IsKeyDownPressing;
        _prevLeft = inputTracker.IsKeyLeftPressing;
        _prevRight = inputTracker.IsKeyRightPressing;
    }
}
