using System;
using System.Numerics;

namespace Alco.GUI;

/// <summary>
/// A virtual list that only renders visible items, with fixed item sizes for efficient scrolling.
/// Items are recycled and repositioned as the user scrolls, providing high performance for large lists.
/// </summary>
public abstract class UIVirtualList<TData> : UINode
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

    private readonly Deque<ActiveItem> _activeItems = new();
    private readonly Pool<UINode> _itemPool;
    private readonly List<TData> _data = new();
    private readonly VirtualContainer _container;
    private readonly UIMask _mask;
    private readonly UIScrollable _scrollable;
    
    private Vector2 _itemSize = new(100f, 50f);
    private Vector2 _spacing = Vector2.Zero;
    private int _columnsPerRow = 1;
    private int _visibleStartIndex = -1;
    private int _visibleEndIndex = -1;
    private Vector2 _lastContainerPosition = new(float.NaN, float.NaN);

    private bool _isLayoutDirty = false;

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
                RefreshVisibleItems();
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
                RefreshVisibleItems();
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
                RefreshVisibleItems();
            }
        }
    }
    
    /// <summary>
    /// Gets the current scroll offset (X and Y) from container position.
    /// In Alco UI: Y+ is up, so positive container Y means scrolled up (toward end of data).
    /// </summary>
    public Vector2 ScrollOffset => new Vector2(-_container.Position.X, _container.Position.Y);
    
    /// <summary>
    /// Gets the current number of data items.
    /// </summary>
    public int Count => _data.Count;
    
    /// <summary>
    /// Gets the internal scrollable viewport.
    /// </summary>
    public UIScrollable Scrollable => _scrollable;
    
    /// <summary>
    /// Gets the total content size.
    /// </summary>
    public Vector2 ContentSize
    {
        get
        {
            if (_data.Count == 0) return Vector2.Zero;
            
            int totalRows = (_data.Count + _columnsPerRow - 1) / _columnsPerRow;
            float width = _columnsPerRow * _itemSize.X + (_columnsPerRow - 1) * _spacing.X;
            float height = totalRows * _itemSize.Y + (totalRows - 1) * _spacing.Y;
            return new Vector2(width, height);
        }
    }
    
    protected UIVirtualList()
    {
        _itemPool = new Pool<UINode>(16, CreateItem);
        
        _mask = new UIMask
        {
            Anchor = Anchor.Stretch,
        };
        
        _scrollable = new UIScrollable
        {
            Anchor = Anchor.Stretch,
            ScrollMode = SrollMode.Vertical
        };
        
        _container = new VirtualContainer(this)
        {
            Anchor = Anchor.TopHorizontalStretch,
            Pivot = Pivot.CenterTop,   // Pivot at center for consistent positioning
        };
        
        // Setup hierarchy: this -> mask -> scrollable -> container
        _mask.Add(_scrollable, false);
        _scrollable.Add(_container, false);
        _scrollable.Content = _container;
        Add(_mask, false);

        // Try to auto-detect item size by creating one prototype item and
        // returning it to the pool, so it can be reused later.
        TryAutoDetectItemSize();
    }
    
    /// <summary>
    /// Factory method to create a new item node.
    /// </summary>
    protected abstract UINode CreateItem();
    
    /// <summary>
    /// Sets the data items for the virtual list.
    /// </summary>
    public void SetItems(IReadOnlyList<TData> items)
    {
        _data.Clear();
        for (int i = 0; i < items.Count; i++)
        {
            _data.Add(items[i]);
        }
        
        // Update container size to represent total content
        Vector2 contentSize = ContentSize;
        _container.Size = contentSize; // Use absolute size instead of SizeDelta
        
        SetLayoutDirty();
        RefreshVisibleItems();
    }

    /// <summary>
    /// Sets the data items for the virtual list from a ReadOnlySpan to minimize allocations.
    /// </summary>
    public void SetItems(ReadOnlySpan<TData> items)
    {
        _data.Clear();
        for (int i = 0; i < items.Length; i++)
        {
            _data.Add(items[i]);
        }

        // Update container size to represent total content
        Vector2 contentSize = ContentSize;
        _container.Size = contentSize;

        SetLayoutDirty();
        RefreshVisibleItems();
    }
    
    /// <summary>
    /// Updates a single item at the specified index.
    /// </summary>
    public void SetItem(int index, TData data)
    {
        if ((uint)index >= (uint)_data.Count)
            return;
            
        _data[index] = data;
        
        // Update the item if it's currently visible
        foreach (var activeItem in _activeItems)
        {
            if (activeItem.Index == index)
            {
                SetDataForItem(activeItem.Node, index, data);
                break;
            }
        }

        SetLayoutDirty();
    }

    /// <summary>
    /// Rebinds all currently active (visible) items using the stored data.
    /// Does not change pooling or layout; only calls SetData on active nodes.
    /// </summary>
    public void RefreshItems()
    {
        foreach (var activeItem in _activeItems)
        {
            int index = activeItem.Index;
            if ((uint)index >= (uint)_data.Count)
            {
                continue;
            }
            SetDataForItem(activeItem.Node, index, _data[index]);
        }

        SetLayoutDirty();
    }

    public void SetLayoutDirty()
    {
        _isLayoutDirty = true;
    }


    /// <summary>
    /// Gets the start index of visible items based on scroll position.
    /// When scrollY=0, container is at center, showing middle portion of data.
    /// </summary>
    protected int GetStartIndex()
    {
        if (_data.Count == 0 || _itemSize.Y <= 0)
            return 0;
            
        float viewportHeight = _mask.Size.Y;
        if (viewportHeight <= 0)
            return 0;
            
        float itemWithSpacingY = _itemSize.Y + _spacing.Y;
        float scrollY = ScrollOffset.Y;
        
        // When scrollY=0, container is centered, so we need to offset by half content height
        float contentHeight = ContentSize.Y;
        float heightDiff = math.max(contentHeight - viewportHeight, 0);
        float centerOffset = heightDiff * (0.5f - _container.Pivot.Y);
        // scrollY is positive when container moves up (toward later items), so we add
        float adjustedScrollY = centerOffset + scrollY;
        
        // Ensure we don't go negative
        adjustedScrollY = Math.Max(0, adjustedScrollY);
        
        // Add small buffer to start rendering items slightly before they become visible
        float bufferedScrollY = Math.Max(0, adjustedScrollY - itemWithSpacingY * 0.5f);
        int startRow = (int)Math.Floor(bufferedScrollY / itemWithSpacingY);
        int startIndex = startRow * _columnsPerRow;
        
        return Math.Max(0, Math.Min(startIndex, _data.Count - 1));
    }
    
    /// <summary>
    /// Gets the end index of visible items based on start index and active count.
    /// </summary>
    protected int GetEndIndex()
    {
        if (_data.Count == 0)
            return -1;
            
        int startIndex = GetStartIndex();
        int activeCount = GetActiveCount();
        int endIndex = startIndex + activeCount - 1;
        
        return Math.Min(endIndex, _data.Count - 1);
    }

    /// <summary>
    /// Gets the number of items that should be active/visible in the current viewport.
    /// </summary>
    protected int GetActiveCount()
    {
        if (_data.Count == 0 || _itemSize.Y <= 0)
            return 0;
            
        float viewportHeight = _mask.Size.Y;
        if (viewportHeight <= 0)
        {
            // Fallback: show reasonable number of items when viewport not available
            return Math.Min(10, _data.Count);
        }
        
        float itemWithSpacingY = _itemSize.Y + _spacing.Y;
        int visibleRows = (int)Math.Ceiling(viewportHeight / itemWithSpacingY) + 1; // +1 for buffer
        int activeItemCount = visibleRows * _columnsPerRow;
        
        return Math.Min(activeItemCount, _data.Count);
    }
    
    /// <summary>
    /// Refreshes the visible items based on current scroll position.
    /// </summary>
    private void RefreshVisibleItems()
    {
        int newStartIndex = GetStartIndex();
        int newEndIndex = GetEndIndex();


        if (!_isLayoutDirty && newStartIndex == _visibleStartIndex && newEndIndex == _visibleEndIndex)
            return;
            
        // Remove items that are no longer visible from the front  
        while (_activeItems.Count > 0 && _activeItems.TryPeekHead(out var headItem) && headItem.Index < newStartIndex)
        {
            if (_activeItems.TryDequeueHead(out var item))
            {
                item.Node.IsEnable = false;
                item.Node.IsLayoutAffected = false;
                _itemPool.TryReturn(item.Node);
            }
        }
        
        // Remove items that are no longer visible from the back
        while (_activeItems.Count > 0 && _activeItems.TryPeekTail(out var tailItem) && tailItem.Index > newEndIndex)
        {
            if (_activeItems.TryDequeueTail(out var item))
            {
                item.Node.IsEnable = false;
                item.Node.IsLayoutAffected = false;
                _itemPool.TryReturn(item.Node);
            }
        }
        
        // Add new items at the front
        while (_activeItems.Count == 0 || (_activeItems.Count > 0 && _activeItems.TryPeekHead(out var headItem) && headItem.Index > newStartIndex))
        {
            int index = _activeItems.Count > 0 && _activeItems.TryPeekHead(out var frontItem) ? frontItem.Index - 1 : newStartIndex;
            if (index < 0 || index >= _data.Count)
                break;
                
            if (!_itemPool.TryGet(out UINode? node) || node == null)
                break;
                
            // Force consistent anchor/pivot settings
            node.Anchor = Anchor.Center;
            node.Pivot = Pivot.Center;
            
            var activeItem = new ActiveItem(node, index);
            PositionItem(activeItem);
            SetDataForItem(node, index, _data[index]);
            
            node.IsEnable = true;
            node.IsLayoutAffected = false; // We handle positioning manually
            _activeItems.EnqueueHead(activeItem);
        }
        
        // Add new items at the back
        while (_activeItems.Count == 0 || (_activeItems.Count > 0 && _activeItems.TryPeekTail(out var tailItem) && tailItem.Index < newEndIndex))
        {
            int index = _activeItems.Count > 0 && _activeItems.TryPeekTail(out var backItem) ? backItem.Index + 1 : newStartIndex;
            if (index < 0 || index >= _data.Count)
                break;
                
            if (!_itemPool.TryGet(out UINode? node) || node == null)
                break;
                
            // Force consistent anchor/pivot settings
            node.Anchor = Anchor.Center;
            node.Pivot = Pivot.Center;
            
            var activeItem = new ActiveItem(node, index);
            PositionItem(activeItem);
            SetDataForItem(node, index, _data[index]);
            
            node.IsEnable = true;
            node.IsLayoutAffected = false; // We handle positioning manually
            _activeItems.EnqueueTail(activeItem);
        }
        
        _visibleStartIndex = newStartIndex;
        _visibleEndIndex = newEndIndex;

        _isLayoutDirty = false;
    }

    /// <summary>
    /// Attempts to initialize <see cref="ItemSize"/> from a prototype item.
    /// This runs once during construction. If the prototype doesn't provide
    /// a positive size, the default size is kept.
    /// </summary>
    private void TryAutoDetectItemSize()
    {
        if (_itemPool.TryGet(out UINode? sample) && sample != null)
        {
            // Use the explicit size on the prototype if available
            Vector2 size = sample.Size;
            if (size.X > 0f && size.Y > 0f)
            {
                _itemSize = size;
            }

            // Keep the prototype disabled and unaffected by layout, then
            // return it to the pool so it can be reused as the first item.
            sample.IsEnable = false;
            sample.IsLayoutAffected = false;
            // _itemPool.TryReturn(sample);
        }
    }

    /// <summary>
    /// Positions an item at its correct location based on its index in the grid.
    /// In Alco UI: Y+ is up, so first row (index 0) should be at the top (positive Y).
    /// </summary>
    private void PositionItem(ActiveItem activeItem)
    {
        int row = activeItem.Index / _columnsPerRow;
        int col = activeItem.Index % _columnsPerRow;
        
        // Calculate grid position - items are positioned relative to container's center
        float x = col * (_itemSize.X + _spacing.X);
        float y = row * (_itemSize.Y + _spacing.Y);
        
        // Force anchor and pivot to avoid calculation issues
        activeItem.Node.Anchor = Anchor.Center; // Position relative to container's center
        activeItem.Node.Pivot = Pivot.Center;   // Item's pivot at center for consistent positioning
                                                // activeItem.Node.Size = _itemSize;       // Set absolute size

        // Calculate offset from container center to grid start position
        Vector2 containerSize = ContentSize;
        float totalGridWidth = _columnsPerRow * _itemSize.X + (_columnsPerRow - 1) * _spacing.X;
        float startX = -totalGridWidth * 0.5f + _itemSize.X * 0.5f;  // Start from left, offset by half item
        float startY = containerSize.Y * 0.5f - _itemSize.Y * 0.5f;  // Start from top (positive Y), offset by half item
        
        // Y+ is up, so subtract y to move down for higher row indices
        activeItem.Node.Position = new Vector2(startX + x, startY - y);
    }
    
    /// <summary>
    /// Binds data to an item if it implements IUIListItem.
    /// </summary>
    protected void SetDataForItem(UINode item, int index, TData data)
    {
        if (item is IUIListItem<TData> uiListItem)
        {
            uiListItem.SetData(index, data);
        }
    }
    
    /// <summary>
    /// Updates the scroll position and refreshes visible items.
    /// </summary>
    protected override void OnRender(Canvas canvas, float delta)
    {

        // Refresh only when container position changes
        Vector2 currentPosition = _container.Position;
        if (currentPosition != _lastContainerPosition){
            _lastContainerPosition = currentPosition;
            RefreshVisibleItems();
        }   

        base.OnRender(canvas, delta);    
    }
    
    private class VirtualContainer : UINode
    {
        private readonly UIVirtualList<TData> _parent;
        
        public VirtualContainer(UIVirtualList<TData> parent)
        {
            _parent = parent;
        }
        
        protected override void OnRender(Canvas canvas, float delta)
        {
            base.OnRender(canvas, delta);
            
            // Ensure all pooled items are added as children
            foreach (var activeItem in _parent._activeItems)
            {
                if (activeItem.Node.Parent != this)
                {
                    Add(activeItem.Node, false);
                }
            }
        }
    }
}