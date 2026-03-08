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
    private readonly UINode _container;

    private Vector2 _itemSize = new(100f, 50f);
    private Vector2 _spacing = Vector2.Zero;
    private int _columnsPerRow = 1;
    private int _currentPage = 0;
    private bool _isLayoutDirty = true;

    private int _focusedIndex = -1;
    private bool _canNavigate;
    private bool _prevUp;
    private bool _prevDown;
    private bool _prevLeft;
    private bool _prevRight;

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
        get => _canNavigate;
        set => _canNavigate = value;
    }

    /// <summary>
    /// Gets the current focused data index within the current page.
    /// Returns -1 if no item is focused.
    /// </summary>
    public int FocusedIndex => _focusedIndex;

    /// <summary>
    /// Gets the internal container that holds the page items.
    /// </summary>
    public UINode Container => _container;

    protected UIPageList()
    {
        _itemPool = new Pool<UINode>(32, CreateItem);

        _container = new UINode
        {
            Anchor = Anchor.Stretch,
            Pivot = Pivot.Center,
        };

        Add(_container);

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
        _focusedIndex = -1;
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
        _focusedIndex = -1;
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
        _focusedIndex = -1;
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
        _focusedIndex = -1;
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
        _focusedIndex = -1;
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

        SetLayoutDirty();
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
            _focusedIndex = -1;
            return;
        }

        int itemsPerPage = GetItemsPerPage();
        if (itemsPerPage <= 0)
        {
            _focusedIndex = -1;
            return;
        }

        int startIndex = _currentPage * itemsPerPage;
        int endIndex = Math.Min(startIndex + itemsPerPage - 1, _data.Count - 1);

        if (index < startIndex || index > endIndex)
        {
            _focusedIndex = -1;
            return;
        }

        _focusedIndex = index;
    }

    /// <summary>
    /// Clears the current focus.
    /// </summary>
    public void ClearFocus()
    {
        _focusedIndex = -1;
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
        foreach (var activeItem in _activeItems)
        {
            activeItem.Node.IsEnable = false;
            activeItem.Node.IsLayoutAffected = false;
            if (activeItem.Node.Parent == _container)
            {
                _container.Remove(activeItem.Node);
            }
            _itemPool.TryReturn(activeItem.Node);
        }
        _activeItems.Clear();

        int itemsPerPage = GetItemsPerPage();
        if (itemsPerPage <= 0 || _data.Count == 0)
        {
            _isLayoutDirty = false;
            return;
        }

        int startIndex = _currentPage * itemsPerPage;
        int endIndex = Math.Min(startIndex + itemsPerPage - 1, _data.Count - 1);

        for (int i = startIndex; i <= endIndex; i++)
        {
            if (!_itemPool.TryGet(out UINode? node) || node == null) break;

            node.Anchor = Anchor.Center;
            node.Pivot = Pivot.Center;

            var activeItem = new ActiveItem(node, i);
            PositionItem(activeItem);
            SetDataForItem(node, i, _data[i]);

            node.IsEnable = true;
            node.IsLayoutAffected = false;
            _container.Add(node, false);
            _activeItems.Add(activeItem);
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

        if (!_canNavigate) return;

        if (canvas.NavigationFocus != this)
        {
            SyncEdgeState(canvas.InputTracker);
            return;
        }

        IUIInputTracker inputTracker = canvas.InputTracker;

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

        bool navigated = false;
        if (_columnsPerRow <= 1)
        {
            if (upEdge) navigated = NavigateByOffset(-1);
            else if (downEdge) navigated = NavigateByOffset(1);
        }
        else
        {
            if (upEdge) navigated = NavigateByOffset(-_columnsPerRow);
            else if (downEdge) navigated = NavigateByOffset(_columnsPerRow);
            else if (leftEdge) navigated = NavigateByOffset(-1);
            else if (rightEdge) navigated = NavigateByOffset(1);
        }

        if (navigated)
        {
            ApplyHover(canvas);
        }
    }

    private void SyncEdgeState(IUIInputTracker inputTracker)
    {
        _prevUp = inputTracker.IsKeyUpPressing;
        _prevDown = inputTracker.IsKeyDownPressing;
        _prevLeft = inputTracker.IsKeyLeftPressing;
        _prevRight = inputTracker.IsKeyRightPressing;
    }

    private bool NavigateByOffset(int offset)
    {
        int itemsPerPage = GetItemsPerPage();
        if (itemsPerPage <= 0) return false;

        int startIndex = _currentPage * itemsPerPage;
        int endIndex = Math.Min(startIndex + itemsPerPage - 1, _data.Count - 1);
        int pageItemCount = endIndex - startIndex + 1;

        if (pageItemCount <= 0) return false;

        if (_focusedIndex < 0)
        {
            _focusedIndex = offset > 0 ? startIndex : endIndex;
            return true;
        }

        int newIndex = _focusedIndex + offset;
        newIndex = Math.Clamp(newIndex, startIndex, endIndex);

        if (newIndex == _focusedIndex) return false;

        _focusedIndex = newIndex;
        return true;
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

    private void ApplyHover(Canvas canvas)
    {
        UINode? node = FindActiveNode(_focusedIndex);
        if (node != null)
        {
            canvas.SetHovered(node);
        }
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
