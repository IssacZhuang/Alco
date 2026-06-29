using System;
using System.Collections.Generic;
using System.Numerics;

namespace Alco.GUI;

/// <summary>
/// A paged list that displays items in pages without scrolling.
/// Items are recycled when navigating between pages.
/// The number of items per page is automatically calculated based on container size, ItemSize, and Spacing.
/// </summary>
public abstract class UIPageList<TData> : UINode, INavigationFocusable, IUIPageList
{
    private struct ActiveItem
    {
        public UINode Node;
        public int Index;

        public ActiveItem(UINode node, int index)
        {
            Node = node;
            Index = index;
        }
    }

    private readonly List<ActiveItem> _activeItems = new();
    private readonly Pool<UINode> _itemPool;
    private readonly List<TData> _data = new();
    private readonly UIMask _mask;
    private readonly UINode _container;

    private readonly NavigationHoverCoordinator _nav;

    private Vector2 _itemSize = new(100f, 50f);
    private Vector2 _spacing = Vector2.Zero;
    private int _columnsPerRow = 1;
    private int _currentPage = 0;
    private bool _isLayoutDirty = true;

    /// <summary>
    /// Gets or sets the fixed size of each item in the grid.
    /// </summary>
    public Vector2 ItemSize
    {
        get => _itemSize;
        set
        {
            if (_itemSize != value)
            {
                _itemSize = value;
                RefreshPage();
            }
        }
    }

    /// <summary>
    /// Gets or sets the spacing between items (X for horizontal, Y for vertical).
    /// </summary>
    public Vector2 Spacing
    {
        get => _spacing;
        set
        {
            if (_spacing != value)
            {
                _spacing = value;
                RefreshPage();
            }
        }
    }

    /// <summary>
    /// Gets or sets the number of columns per row in the grid.
    /// </summary>
    public int ColumnsPerRow
    {
        get => _columnsPerRow;
        set
        {
            if (_columnsPerRow != value && value > 0)
            {
                _columnsPerRow = value;
                RefreshPage();
            }
        }
    }

    /// <summary>
    /// Gets the current page index (0-based).
    /// </summary>
    public int CurrentPage => _currentPage;

    /// <summary>
    /// Gets the total number of pages.
    /// </summary>
    public int TotalPages
    {
        get
        {
            int itemsPerPage = GetItemsPerPage();
            if (itemsPerPage <= 0) return 0;
            if (_data.Count == 0) return 0;
            return (_data.Count + itemsPerPage - 1) / itemsPerPage;
        }
    }

    /// <summary>
    /// Gets whether there is a previous page available.
    /// </summary>
    public bool HasPreviousPage => _currentPage > 0;

    /// <summary>
    /// Gets whether there is a next page available.
    /// </summary>
    public bool HasNextPage => _currentPage < TotalPages - 1;

    /// <summary>
    /// Gets the current number of data items.
    /// </summary>
    public int Count => _data.Count;

    /// <summary>
    /// Gets or sets whether this page list can process keyboard navigation input.
    /// </summary>
    public bool CanNavigate
    {
        get => _nav.CanNavigate;
        set => _nav.CanNavigate = value;
    }

    /// <summary>
    /// Gets the current focused data index within the current page.
    /// Returns -1 if no item is focused.
    /// </summary>
    public int FocusedIndex => _nav.FocusedIndex;

    /// <summary>
    /// Gets the internal container that holds the page items.
    /// </summary>
    public UINode Container => _container;

    protected UIPageList()
    {
        _nav = new NavigationHoverCoordinator(this)
        {
            ResolveNode = FindActiveNode,
            TryNavigate = TryNavigateByDirection,
            IndexOfHoveredChild = IndexOfHoveredChild,
            OnNavigated = (ctx, idx) => OnNavigated((Canvas)ctx, idx),
        };

        _itemPool = new Pool<UINode>(32, CreateItem);

        _mask = new UIMask
        {
            Anchor = Anchor.Stretch
        };

        _container = new UINode
        {
            Anchor = Anchor.Stretch,
            Pivot = Pivot.Center,
        };

        _mask.Add(_container);
        Add(_mask);

        TryAutoDetectItemSize();
    }

    /// <summary>
    /// Factory method to create a new item node.
    /// </summary>
    protected abstract UINode CreateItem();

    /// <summary>
    /// Sets the data items for the page list.
    /// </summary>
    public void SetItems(IReadOnlyList<TData> items)
    {
        _data.Clear();
        for (int i = 0; i < items.Count; i++)
        {
            _data.Add(items[i]);
        }

        _currentPage = 0;
        _nav.ClearFocus();
        RefreshPage();
    }

    /// <summary>
    /// Sets the data items for the page list from a ReadOnlySpan to minimize allocations.
    /// </summary>
    public void SetItems(ReadOnlySpan<TData> items)
    {
        _data.Clear();
        for (int i = 0; i < items.Length; i++)
        {
            _data.Add(items[i]);
        }

        _currentPage = 0;
        _nav.ClearFocus();
        RefreshPage();
    }

    /// <summary>
    /// Navigates to the previous page.
    /// </summary>
    /// <returns>True if navigation succeeded, false if already at first page.</returns>
    public bool PreviousPage()
    {
        if (!HasPreviousPage) return false;
        _currentPage--;
        _nav.ClearFocus();
        RefreshPage();
        return true;
    }

    /// <summary>
    /// Navigates to the next page.
    /// </summary>
    /// <returns>True if navigation succeeded, false if already at last page.</returns>
    public bool NextPage()
    {
        if (!HasNextPage) return false;
        _currentPage++;
        _nav.ClearFocus();
        RefreshPage();
        return true;
    }

    /// <summary>
    /// Sets the current page to the specified page index.
    /// </summary>
    /// <param name="page">The page index (0-based).</param>
    /// <returns>True if navigation succeeded, false if page index is invalid.</returns>
    public bool SetPage(int page)
    {
        int totalPages = TotalPages;
        if (page < 0 || page >= totalPages) return false;
        if (page == _currentPage) return true;

        _currentPage = page;
        _nav.ClearFocus();
        RefreshPage();
        return true;
    }

    /// <summary>
    /// Rebinds all currently active items using the stored data.
    /// </summary>
    public void RefreshItems()
    {
        foreach (var activeItem in _activeItems)
        {
            int index = activeItem.Index;
            if ((uint)index >= (uint)_data.Count) continue;
            SetDataForItem(activeItem.Node, index, _data[index]);
        }

    }

    /// <summary>
    /// Sets focus to the data item at the specified index within the current page.
    /// The index is clamped to valid range. Pass -1 to clear focus.
    /// </summary>
    /// <param name="index">The data index to focus, or -1 to clear.</param>
    public void SetFocus(int index)
    {
        if (index < 0)
        {
            _nav.ClearFocus();
            return;
        }

        int itemsPerPage = GetItemsPerPage();
        if (itemsPerPage <= 0)
        {
            _nav.ClearFocus();
            return;
        }

        int startIndex = _currentPage * itemsPerPage;
        int endIndex = Math.Min(startIndex + itemsPerPage - 1, _data.Count - 1);

        if (index < startIndex || index > endIndex)
        {
            _nav.ClearFocus();
            return;
        }

        _nav.SetFocus(index);
    }

    /// <summary>
    /// Clears the current focus.
    /// </summary>
    public void ClearFocus()
    {
        _nav.ClearFocus();
    }

    public void SetLayoutDirty()
    {
        _isLayoutDirty = true;
    }

    /// <summary>
    /// Gets the number of items displayed per page based on container size and item configuration.
    /// </summary>
    /// <returns>The number of items per page.</returns>
    public int GetItemsPerPage()
    {
        if (_itemSize.Y <= 0) return 0;

        float containerHeight = _container.Size.Y;
        if (containerHeight <= 0) return _columnsPerRow;

        float itemWithSpacingY = _itemSize.Y + _spacing.Y;
        int rows = (int)((containerHeight + _spacing.Y) / itemWithSpacingY);
        if (rows <= 0) rows = 1;

        return rows * _columnsPerRow;
    }

    private void RefreshPage()
    {
        int itemsPerPage = GetItemsPerPage();
        int neededCount;
        int startIndex;

        if (itemsPerPage <= 0 || _data.Count == 0)
        {
            neededCount = 0;
            startIndex = 0;
        }
        else
        {
            startIndex = _currentPage * itemsPerPage;
            int endIndex = Math.Min(startIndex + itemsPerPage - 1, _data.Count - 1);
            neededCount = endIndex - startIndex + 1;
        }

        // Return excess items to pool (remove from tail to preserve earlier items)
        while (_activeItems.Count > neededCount)
        {
            int lastIdx = _activeItems.Count - 1;
            var last = _activeItems[lastIdx];
            last.Node.IsEnable = false;
            last.Node.IsLayoutAffected = false;
            if (last.Node.Parent == _container)
            {
                _container.Remove(last.Node);
            }
            _itemPool.TryReturn(last.Node);
            _activeItems.RemoveAt(lastIdx);
        }

        // Get more items from pool if needed
        while (_activeItems.Count < neededCount)
        {
            if (!_itemPool.TryGet(out UINode? node) || node == null) break;

            node.IsEnable = true;
            node.IsLayoutAffected = false;
            _container.Add(node, false);
            _activeItems.Add(new ActiveItem(node, 0));
        }

        // Update all active items with correct data and positions
        for (int i = 0; i < _activeItems.Count; i++)
        {
            int dataIndex = startIndex + i;
            var activeItem = new ActiveItem(_activeItems[i].Node, dataIndex);
            _activeItems[i] = activeItem;

            PositionItem(activeItem);
            SetDataForItem(activeItem.Node, dataIndex, _data[dataIndex]);
        }

        _isLayoutDirty = false;
    }

    private void TryAutoDetectItemSize()
    {
        if (_itemPool.TryGet(out UINode? sample) && sample != null)
        {
            Vector2 size = sample.Size;
            if (size.X > 0f && size.Y > 0f)
            {
                _itemSize = size;
            }

            sample.IsEnable = false;
            sample.IsLayoutAffected = false;
        }
    }

    private void PositionItem(ActiveItem activeItem)
    {
        int localIndex = activeItem.Index % GetItemsPerPage();
        int row = localIndex / _columnsPerRow;
        int col = localIndex % _columnsPerRow;

        float x = col * (_itemSize.X + _spacing.X);
        float y = row * (_itemSize.Y + _spacing.Y);

        activeItem.Node.Anchor = Anchor.Center;
        activeItem.Node.Pivot = Pivot.Center;

        float totalGridWidth = _columnsPerRow * _itemSize.X + (_columnsPerRow - 1) * _spacing.X;
        int rowsPerPage = GetItemsPerPage() / _columnsPerRow;
        float totalGridHeight = rowsPerPage * _itemSize.Y + (rowsPerPage - 1) * _spacing.Y;

        float startX = -totalGridWidth * 0.5f + _itemSize.X * 0.5f;
        float startY = totalGridHeight * 0.5f - _itemSize.Y * 0.5f;

        activeItem.Node.Position = new Vector2(startX + x, startY - y);
    }

    private void SetDataForItem(UINode item, int index, TData data)
    {
        if (item is IUIListItem<TData> uiListItem)
        {
            uiListItem.SetData(index, data);
        }
    }

    protected override void OnTick(Canvas canvas, float delta)
    {
        base.OnTick(canvas, delta);
        _nav.Orientation = _columnsPerRow <= 1 ? NavOrientation.Vertical : NavOrientation.Grid;
        _nav.Tick(canvas);
    }

    /// <summary>
    /// Called after a successful keyboard navigation.
    /// The default implementation applies hover to the focused item.
    /// Override to customize post-navigation behavior (e.g. update selection).
    /// </summary>
    /// <param name="canvas">The current canvas.</param>
    /// <param name="focusIndex">The new focused data index.</param>
    protected virtual void OnNavigated(Canvas canvas, int focusIndex)
    {
        UINode? node = FindActiveNode(focusIndex);
        if (node != null)
        {
            canvas.SetHovered(node);
        }
    }

    /// <summary>
    /// Navigation callback: moves the focus index one step in <paramref name="direction"/>
    /// from <paramref name="fromIndex"/>, honoring single-column vs grid layout and page bounds.
    /// </summary>
    /// <param name="direction">The navigation direction.</param>
    /// <param name="fromIndex">The index to move from (-1 when nothing is focused).</param>
    /// <returns>The new focused index, or null when the move is blocked.</returns>
    private int? TryNavigateByDirection(NavDirection direction, int fromIndex)
    {
        int offset = direction switch
        {
            NavDirection.Up => _columnsPerRow <= 1 ? -1 : -_columnsPerRow,
            NavDirection.Down => _columnsPerRow <= 1 ? 1 : _columnsPerRow,
            NavDirection.Left => -1,
            NavDirection.Right => 1,
            _ => 0,
        };
        return NavigateByOffset(fromIndex, offset);
    }

    /// <summary>
    /// Moves the focus index by the given offset, clamping to the current page range.
    /// </summary>
    /// <param name="fromIndex">The index to move from (-1 when nothing is focused).</param>
    /// <param name="offset">The offset to apply (e.g. +1, -1, +columnsPerRow).</param>
    /// <returns>The new focused index, or null when focus did not change.</returns>
    private int? NavigateByOffset(int fromIndex, int offset)
    {
        int itemsPerPage = GetItemsPerPage();
        if (itemsPerPage <= 0)
        {
            return null;
        }

        int startIndex = _currentPage * itemsPerPage;
        int endIndex = Math.Min(startIndex + itemsPerPage - 1, _data.Count - 1);
        int pageItemCount = endIndex - startIndex + 1;

        if (pageItemCount <= 0)
        {
            return null;
        }

        if (fromIndex < 0)
        {
            return offset > 0 ? startIndex : endIndex;
        }

        int newIndex = Math.Clamp(fromIndex + offset, startIndex, endIndex);
        return newIndex == fromIndex ? null : newIndex;
    }

    /// <summary>
    /// When nothing is focused, resolves the hovered active item's data index to
    /// start navigation from. Walks the parent chain so hovering a child of an
    /// item still counts.
    /// </summary>
    /// <param name="hovered">The currently hovered node.</param>
    /// <returns>The data index of the hovered item, or null.</returns>
    private int? IndexOfHoveredChild(UINode? hovered)
    {
        if (hovered == null)
        {
            return null;
        }

        foreach (var activeItem in _activeItems)
        {
            if (NavigationHoverCoordinator.IsHoveredWithin(hovered, activeItem.Node))
            {
                return activeItem.Index;
            }
        }
        return null;
    }

    private UINode? FindActiveNode(int dataIndex)
    {
        foreach (var activeItem in _activeItems)
        {
            if (activeItem.Index == dataIndex)
            {
                return activeItem.Node;
            }
        }
        return null;
    }

    protected override void OnUpdate(Canvas canvas, float delta)
    {
        if (_isLayoutDirty)
        {
            RefreshPage();
        }

        base.OnUpdate(canvas, delta);
    }
}
