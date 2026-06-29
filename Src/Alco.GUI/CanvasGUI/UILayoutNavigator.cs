using System.Numerics;

namespace Alco.GUI;

/// <summary>
/// A <see cref="UILayout"/> with built-in D-Pad / arrow-key navigation.
/// Tracks a focused index and programmatically sets the hovered node on the canvas,
/// enabling gamepad-style menu navigation. Hover/focus coordination and edge
/// detection are delegated to a <see cref="NavigationHoverCoordinator"/>.
/// </summary>
public class UILayoutNavigator : UILayout, INavigationFocusable
{
    private readonly NavigationHoverCoordinator _nav;

    /// <summary>
    /// Gets or sets whether this navigator can process navigation input.
    /// </summary>
    public bool CanNavigate
    {
        get => _nav.CanNavigate;
        set => _nav.CanNavigate = value;
    }

    /// <summary>
    /// Gets the current focused index within the layout's children.
    /// Returns -1 if no child is focused.
    /// </summary>
    public int FocusedIndex => _nav.FocusedIndex;

    /// <summary>
    /// Gets the currently focused node, or null if none is focused.
    /// </summary>
    public UINode? FocusedNode => _nav.FocusedNode;

    /// <summary>
    /// Initializes a new instance of the <see cref="UILayoutNavigator"/> class.
    /// </summary>
    public UILayoutNavigator()
    {
        _nav = new NavigationHoverCoordinator(this)
        {
            ResolveNode = ResolveChild,
            TryNavigate = Navigate,
            IndexOfHoveredChild = IndexOfHoveredChild,
        };
    }

    /// <summary>
    /// Sets focus to the child at the specified index.
    /// The index is clamped to valid range. Pass -1 to clear focus.
    /// </summary>
    /// <param name="index">The index of the child to focus, or -1 to clear.</param>
    public void SetFocus(int index)
    {
        _nav.SetFocus(index);
    }

    /// <summary>
    /// Clears the current focus.
    /// </summary>
    public void ClearFocus()
    {
        _nav.ClearFocus();
    }

    /// <inheritdoc/>
    protected override void OnTick(Canvas canvas, float delta)
    {
        base.OnTick(canvas, delta);
        _nav.Orientation = ToNavOrientation(LayoutType);
        _nav.Tick(canvas);
    }

    private static NavOrientation ToNavOrientation(LayoutType layoutType)
    {
        return layoutType switch
        {
            LayoutType.Vertical => NavOrientation.Vertical,
            LayoutType.Horizontal => NavOrientation.Horizontal,
            LayoutType.Grid => NavOrientation.Grid,
            _ => NavOrientation.Vertical,
        };
    }

    private UINode? ResolveChild(int index)
    {
        return index >= 0 && index < Children.Count ? Children[index] : null;
    }

    private int? Navigate(NavDirection direction, int fromIndex)
    {
        int childCount = Children.Count;
        if (childCount == 0)
        {
            return null;
        }

        if (LayoutType == LayoutType.Grid)
        {
            return NavigateGrid(direction, fromIndex);
        }

        int step = direction == NavDirection.Up || direction == NavDirection.Left ? -1 : 1;

        if (fromIndex < 0)
        {
            // Nothing focused: jump to the list edge in the requested direction.
            return TryFocusFrom(step < 0 ? childCount - 1 : 0, step);
        }

        return TryFocusFrom(fromIndex + step, step);
    }

    private int? NavigateGrid(NavDirection direction, int fromIndex)
    {
        int count = GetNavigableCount();
        if (count == 0)
        {
            return null;
        }

        if (fromIndex < 0)
        {
            return TryFocusFrom(0, 1);
        }

        int columnsPerRow = CalculateGridColumns();
        if (columnsPerRow <= 0)
        {
            columnsPerRow = 1;
        }

        int navigableIndex = GetNavigableIndex(fromIndex);
        if (navigableIndex < 0)
        {
            return TryFocusFrom(0, 1);
        }

        int col = navigableIndex % columnsPerRow;
        int row = navigableIndex / columnsPerRow;

        int newCol = col;
        int newRow = row;
        switch (direction)
        {
            case NavDirection.Left:
                newCol = col - 1;
                break;
            case NavDirection.Right:
                newCol = col + 1;
                break;
            case NavDirection.Up:
                newRow = row - 1;
                break;
            case NavDirection.Down:
                newRow = row + 1;
                break;
        }

        if (newCol < 0 || newCol >= columnsPerRow)
        {
            return null;
        }

        int totalRows = (count + columnsPerRow - 1) / columnsPerRow;
        if (newRow < 0 || newRow >= totalRows)
        {
            return null;
        }

        int newNavigableIndex = newRow * columnsPerRow + newCol;
        if (newNavigableIndex >= count)
        {
            return null;
        }

        return GetChildIndexFromNavigable(newNavigableIndex);
    }

    /// <summary>
    /// Tries to focus starting from a given index, searching in the specified direction.
    /// Skips non-navigable children.
    /// </summary>
    /// <param name="startIndex">The index to start searching from.</param>
    /// <param name="direction">Search direction: +1 forward, -1 backward.</param>
    /// <returns>The focused index, or null if none found.</returns>
    private int? TryFocusFrom(int startIndex, int direction)
    {
        int childCount = Children.Count;
        int i = startIndex;

        while (i >= 0 && i < childCount)
        {
            if (IsNavigable(Children[i]))
            {
                return i;
            }
            i += direction;
        }

        return null;
    }

    /// <summary>
    /// When nothing is focused, resolves the hovered navigable child to focus from.
    /// Walks the parent chain so hovering a child of a row still counts.
    /// </summary>
    /// <param name="hovered">The currently hovered node.</param>
    /// <returns>The child index of the hovered navigable node, or null.</returns>
    private int? IndexOfHoveredChild(UINode? hovered)
    {
        if (hovered == null)
        {
            return null;
        }

        UINode? node = hovered.FirstAncestorWhere(n =>
            n.Parent == this && IsNavigable(n));
        if (node == null)
        {
            return null;
        }

        for (int i = 0; i < Children.Count; i++)
        {
            if (ReferenceEquals(Children[i], node))
            {
                return i;
            }
        }
        return null;
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
