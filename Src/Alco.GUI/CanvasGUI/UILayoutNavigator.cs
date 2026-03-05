using System.Numerics;

namespace Alco.GUI;

/// <summary>
/// A <see cref="UILayout"/> with built-in D-Pad / arrow-key navigation.
/// Tracks a focused index and programmatically sets the hovered node on the canvas,
/// enabling gamepad-style menu navigation.
/// Input is read from the canvas <see cref="IUIInputTracker"/> automatically.
/// </summary>
public class UILayoutNavigator : UILayout
{
    private int _focusedIndex = -1;
    private bool _canNavigate = true;

    // Edge detection for navigation directions
    private bool _prevUp;
    private bool _prevDown;
    private bool _prevLeft;
    private bool _prevRight;

/// <summary>
    /// Gets or sets whether this navigator can process navigation input.
    /// </summary>
    public bool CanNavigate
    {
        get => _canNavigate;
        set => _canNavigate = value;
    }

    /// <summary>
    /// Gets the current focused index within the layout's children.
    /// Returns -1 if no child is focused.
    /// </summary>
    public int FocusedIndex => _focusedIndex;

    /// <summary>
    /// Gets the currently focused node, or null if none is focused.
    /// </summary>
    public UINode? FocusedNode =>
        _focusedIndex >= 0 && _focusedIndex < Children.Count
            ? Children[_focusedIndex]
            : null;

    /// <summary>
    /// Initializes a new instance of the <see cref="UILayoutNavigator"/> class.
    /// </summary>
    public UILayoutNavigator()
    {
    }

    /// <summary>
    /// Sets focus to the child at the specified index.
    /// The index is clamped to valid range. Pass -1 to clear focus.
    /// </summary>
    /// <param name="index">The index of the child to focus, or -1 to clear.</param>
    public void SetFocus(int index)
    {
        if (index < 0)
        {
            _focusedIndex = -1;
            return;
        }

        int count = Children.Count;
        if (count == 0)
        {
            _focusedIndex = -1;
            return;
        }

        _focusedIndex = Math.Clamp(index, 0, count - 1);
    }

    /// <summary>
    /// Clears the current focus.
    /// </summary>
    public void ClearFocus()
    {
        _focusedIndex = -1;
    }

    /// <inheritdoc/>
    protected override void OnTick(Canvas canvas, float delta)
    {
        base.OnTick(canvas, delta);

        if (!_canNavigate)
        {
            return;
        }

        IUIInputTracker inputTracker = canvas.InputTracker;

        // Read raw directional input
        bool up = inputTracker.IsKeyUpPressing;
        bool down = inputTracker.IsKeyDownPressing;
        bool left = inputTracker.IsKeyLeftPressing;
        bool right = inputTracker.IsKeyRightPressing;

        // Edge detection: trigger only on rising edge
        bool upEdge = up && !_prevUp;
        bool downEdge = down && !_prevDown;
        bool leftEdge = left && !_prevLeft;
        bool rightEdge = right && !_prevRight;

        _prevUp = up;
        _prevDown = down;
        _prevLeft = left;
        _prevRight = right;

        // Determine navigation direction based on layout type
        bool navigated = false;
        switch (LayoutType)
        {
            case LayoutType.Vertical:
                if (upEdge) navigated = NavigatePrevious();
                else if (downEdge) navigated = NavigateNext();
                break;

            case LayoutType.Horizontal:
                if (leftEdge) navigated = NavigatePrevious();
                else if (rightEdge) navigated = NavigateNext();
                break;

            case LayoutType.Grid:
                if (upEdge) navigated = NavigateGrid(0, -1);
                else if (downEdge) navigated = NavigateGrid(0, 1);
                else if (leftEdge) navigated = NavigateGrid(-1, 0);
                else if (rightEdge) navigated = NavigateGrid(1, 0);
                break;
        }

        if (navigated)
        {
            ApplyHover(canvas);
        }
    }

    /// <summary>
    /// Navigates to the previous navigable child.
    /// </summary>
    /// <returns>True if focus changed.</returns>
    private bool NavigatePrevious()
    {
        int childCount = Children.Count;
        if (childCount == 0) return false;

        // If nothing focused, focus the last navigable item
        if (_focusedIndex < 0)
        {
            return TryFocusFrom(childCount - 1, -1);
        }

        return TryFocusFrom(_focusedIndex - 1, -1);
    }

    /// <summary>
    /// Navigates to the next navigable child.
    /// </summary>
    /// <returns>True if focus changed.</returns>
    private bool NavigateNext()
    {
        int childCount = Children.Count;
        if (childCount == 0) return false;

        // If nothing focused, focus the first navigable item
        if (_focusedIndex < 0)
        {
            return TryFocusFrom(0, 1);
        }

        return TryFocusFrom(_focusedIndex + 1, 1);
    }

    /// <summary>
    /// Navigates within a grid layout by column/row offset.
    /// </summary>
    /// <param name="colDelta">Column offset (-1 for left, +1 for right, 0 for none).</param>
    /// <param name="rowDelta">Row offset (-1 for up, +1 for down, 0 for none).</param>
    /// <returns>True if focus changed.</returns>
    private bool NavigateGrid(int colDelta, int rowDelta)
    {
        int count = GetNavigableCount();
        if (count == 0) return false;

        // If nothing focused, focus first navigable item
        if (_focusedIndex < 0)
        {
            return TryFocusFrom(0, 1);
        }

        int columnsPerRow = CalculateGridColumns();
        if (columnsPerRow <= 0) columnsPerRow = 1;

        // Convert current focused index to navigable-space index
        int navigableIndex = GetNavigableIndex(_focusedIndex);
        if (navigableIndex < 0) return TryFocusFrom(0, 1);

        int col = navigableIndex % columnsPerRow;
        int row = navigableIndex / columnsPerRow;

        int newCol = col + colDelta;
        int newRow = row + rowDelta;

        // Clamp column
        if (newCol < 0 || newCol >= columnsPerRow) return false;

        int totalRows = (count + columnsPerRow - 1) / columnsPerRow;
        if (newRow < 0 || newRow >= totalRows) return false;

        int newNavigableIndex = newRow * columnsPerRow + newCol;
        if (newNavigableIndex >= count) return false;

        // Convert navigable-space index back to children index
        int childIndex = GetChildIndexFromNavigable(newNavigableIndex);
        if (childIndex < 0) return false;

        _focusedIndex = childIndex;
        return true;
    }

    /// <summary>
    /// Tries to focus starting from a given index, searching in the specified direction.
    /// Skips non-navigable children.
    /// </summary>
    /// <param name="startIndex">The index to start searching from.</param>
    /// <param name="direction">Search direction: +1 forward, -1 backward.</param>
    /// <returns>True if a navigable child was found and focused.</returns>
    private bool TryFocusFrom(int startIndex, int direction)
    {
        int childCount = Children.Count;
        int i = startIndex;

        while (i >= 0 && i < childCount)
        {
            if (IsNavigable(Children[i]))
            {
                _focusedIndex = i;
                return true;
            }
            i += direction;
        }

        return false;
    }

    /// <summary>
    /// Applies the current focus to the canvas hover system.
    /// </summary>
    private void ApplyHover(Canvas canvas)
    {
        UINode? focused = FocusedNode;
        if (focused != null && IsNavigable(focused))
        {
            canvas.SetHovered(focused);
        }
    }

    /// <summary>
    /// Determines whether a child node can receive navigation focus.
    /// </summary>
    private static bool IsNavigable(UINode node)
    {
        if (!node.IsEnable) return false;
        if (node is UISelectable selectable && !selectable.IsInteractable) return false;
        return true;
    }

    /// <summary>
    /// Counts navigable children in the layout.
    /// </summary>
    private int GetNavigableCount()
    {
        int count = 0;
        for (int i = 0; i < Children.Count; i++)
        {
            if (IsNavigable(Children[i]))
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>
    /// Gets the navigable-space index for a given children index.
    /// Returns -1 if the child is not navigable.
    /// </summary>
    private int GetNavigableIndex(int childIndex)
    {
        int navigable = 0;
        for (int i = 0; i < Children.Count; i++)
        {
            if (i == childIndex)
            {
                return IsNavigable(Children[i]) ? navigable : -1;
            }
            if (IsNavigable(Children[i]))
            {
                navigable++;
            }
        }
        return -1;
    }

    /// <summary>
    /// Converts a navigable-space index back to the actual children index.
    /// </summary>
    private int GetChildIndexFromNavigable(int navigableIndex)
    {
        int navigable = 0;
        for (int i = 0; i < Children.Count; i++)
        {
            if (IsNavigable(Children[i]))
            {
                if (navigable == navigableIndex)
                {
                    return i;
                }
                navigable++;
            }
        }
        return -1;
    }

    /// <summary>
    /// Calculates the number of columns in the grid layout.
    /// Mirrors the logic in <see cref="UILayout.UpdateGridLayout"/>.
    /// </summary>
    private int CalculateGridColumns()
    {
        float availableWidth = Size.X - Padding.Horizontal;

        // Determine item width
        float itemWidth;
        if (IsFixedItemSize)
        {
            itemWidth = FixedItemWidth;
        }
        else
        {
            // Use first navigable child's width as reference
            for (int i = 0; i < Children.Count; i++)
            {
                if (IsNavigable(Children[i]))
                {
                    itemWidth = Children[i].RenderSize.X;
                    return Math.Max(1, (int)((availableWidth + Spacing.X) / (itemWidth + Spacing.X)));
                }
            }
            return 1;
        }

        return Math.Max(1, (int)((availableWidth + Spacing.X) / (itemWidth + Spacing.X)));
    }
}
