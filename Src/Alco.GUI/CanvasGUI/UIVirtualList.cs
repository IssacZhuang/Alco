using System.Numerics;

namespace Alco.GUI;

/// <summary>
/// A high-performance virtual list that only renders visible items in a grid layout.
/// Supports fixed item sizes only for optimal performance.
/// Vertical layout is treated as a single-column grid.
/// </summary>
public abstract class UIVirtualList<TData> : UINode
{
    private readonly List<UINode> _itemPool = new();
    private readonly Dictionary<int, UINode> _visibleItems = new();
    private readonly List<TData> _data = new();
    
    private readonly UIMask _mask;
    private readonly UIScrollable _scrollable;
    private readonly VirtualContainer _container;
    
    // Grid configuration
    private int _columnsPerRow = 1;
    private Vector2 _itemSize = new(100f, 100f);
    private Vector2 _spacing = Vector2.Zero;
    private float _paddingTop = 0f;
    private float _paddingBottom = 0f;
    private float _paddingLeft = 0f;
    private float _paddingRight = 0f;
    
    // Virtual rendering state
    private int _firstVisibleRow = 0;
    private int _lastVisibleRow = 0;
    private int _totalRows = 0;
    private float _totalContentHeight = 0f;
    
    /// <summary>
    /// Number of columns per row in the grid layout. Set to 1 for vertical list.
    /// </summary>
    public int ColumnsPerRow
    {
        get => _columnsPerRow;
        set
        {
            if (value < 1) value = 1;
            _columnsPerRow = value;
            InvalidateLayout();
        }
    }
    
    /// <summary>
    /// Fixed size for each item in the list.
    /// </summary>
    public Vector2 ItemSize
    {
        get => _itemSize;
        set
        {
            _itemSize = value;
            InvalidateLayout();
        }
    }
    
    /// <summary>
    /// Spacing between items (X: horizontal, Y: vertical).
    /// </summary>
    public Vector2 Spacing
    {
        get => _spacing;
        set
        {
            _spacing = value;
            InvalidateLayout();
        }
    }
    
    /// <summary>
    /// Top padding for the content area.
    /// </summary>
    public float PaddingTop
    {
        get => _paddingTop;
        set
        {
            _paddingTop = value;
            InvalidateLayout();
        }
    }
    
    /// <summary>
    /// Bottom padding for the content area.
    /// </summary>
    public float PaddingBottom
    {
        get => _paddingBottom;
        set
        {
            _paddingBottom = value;
            InvalidateLayout();
        }
    }
    
    /// <summary>
    /// Left padding for the content area.
    /// </summary>
    public float PaddingLeft
    {
        get => _paddingLeft;
        set
        {
            _paddingLeft = value;
            InvalidateLayout();
        }
    }
    
    /// <summary>
    /// Right padding for the content area.
    /// </summary>
    public float PaddingRight
    {
        get => _paddingRight;
        set
        {
            _paddingRight = value;
            InvalidateLayout();
        }
    }
    
    /// <summary>
    /// Current number of data items.
    /// </summary>
    public int Count => _data.Count;
    
    protected UIVirtualList()
    {
        _mask = new UIMask
        {
            Anchor = Anchor.Stretch
        };
        
        _scrollable = new UIScrollable
        {
            Anchor = Anchor.Stretch,
            ScrollMode = SrollMode.Vertical
        };
        
        _container = new VirtualContainer(this)
        {
            Anchor = Anchor.Stretch
        };
        
        // Setup hierarchy: this -> mask -> scrollable -> container
        _mask.Add(_scrollable, false);
        _scrollable.Add(_container, false);
        _scrollable.Content = _container;
        Add(_mask, false);
    }
    
    /// <summary>
    /// Factory method to create a new item node.
    /// </summary>
    protected abstract UINode CreateItem();
    
    /// <summary>
    /// Sets the data for the virtual list and triggers a refresh.
    /// </summary>
    public void SetItems(IReadOnlyList<TData> items)
    {
        _data.Clear();
        for (int i = 0; i < items.Count; i++)
        {
            _data.Add(items[i]);
        }
        
        InvalidateLayout();
        UpdateVirtualView();
    }
    
    /// <summary>
    /// Updates a single item's data if it's currently visible.
    /// </summary>
    public void SetItem(int index, TData data)
    {
        if ((uint)index >= (uint)_data.Count) return;
        
        _data[index] = data;
        
        // Update the item if it's currently visible
        if (_visibleItems.TryGetValue(index, out UINode? item))
        {
            SetDataForItem(item, index, data);
        }
    }
    
    private void InvalidateLayout()
    {
        CalculateLayoutMetrics();
        UpdateContainerSize();
    }
    
    private void CalculateLayoutMetrics()
    {
        if (_data.Count == 0)
        {
            _totalRows = 0;
            _totalContentHeight = _paddingTop + _paddingBottom;
            return;
        }
        
        _totalRows = (_data.Count + _columnsPerRow - 1) / _columnsPerRow;
        
        float rowHeight = _itemSize.Y + _spacing.Y;
        _totalContentHeight = _paddingTop + _paddingBottom + 
                             _totalRows * rowHeight - _spacing.Y; // Remove last spacing
    }
    
    private void UpdateContainerSize()
    {
        // For virtual list, container should simulate the full content height for scrolling
        // This allows the scrollbar to work correctly even with millions of items
        _container.Size = new Vector2(_container.Size.X, _totalContentHeight);
    }
    
    private void UpdateVirtualView()
    {
        if (_data.Count == 0)
        {
            ClearActiveItems();
            return;
        }
        
        CalculateVisibleRange();
        UpdateActiveItems();
    }
    
    private void CalculateVisibleRange()
    {
        float viewportHeight = _mask.Size.Y;
        float scrollOffset = -_container.Position.Y; // Container moves up when scrolling down
        
        float rowHeight = _itemSize.Y + _spacing.Y;
        
        // Calculate visible row range with buffer
        int bufferRows = 1; // Render 1 extra row above and below
        _firstVisibleRow = Math.Max(0, 
            (int)Math.Floor((scrollOffset - _paddingTop) / rowHeight) - bufferRows);
        _lastVisibleRow = Math.Min(_totalRows - 1,
            (int)Math.Ceiling((scrollOffset - _paddingTop + viewportHeight) / rowHeight) + bufferRows);
    }
    
    private void UpdateActiveItems()
    {
        // Return all visible items to pool
        foreach (var kvp in _visibleItems)
        {
            _itemPool.Add(kvp.Value);
            _container.RemoveChild(kvp.Value);
        }
        _visibleItems.Clear();
        
        // Create items for visible range
        for (int row = _firstVisibleRow; row <= _lastVisibleRow; row++)
        {
            for (int col = 0; col < _columnsPerRow; col++)
            {
                int dataIndex = row * _columnsPerRow + col;
                if (dataIndex >= _data.Count) break;
                
                CreateAndPositionItem(dataIndex, row, col);
            }
        }
    }
    
    private void CreateAndPositionItem(int dataIndex, int row, int col)
    {
        // Get item from pool or create new
        UINode item;
        if (_itemPool.Count > 0)
        {
            item = _itemPool[_itemPool.Count - 1];
            _itemPool.RemoveAt(_itemPool.Count - 1);
        }
        else
        {
            item = CreateItem();
        }
        
        // Set item properties like UILayout does
        item.Size = _itemSize;
        
        // For vertical-style grid layout, use similar anchor/pivot as UILayout
        if (_columnsPerRow == 1)
        {
            // Pure vertical: destretch vertical, keep horizontal stretching
            item.Anchor = Anchor.DestretchVertical(item.Anchor);
            item.Pivot = new Pivot(item.Pivot.X, -0.5f);
        }
        else
        {
            // Grid: use center anchor like UILayout grid
            item.Anchor = Anchor.Center;
            item.Pivot = new Pivot(0f, -0.5f);
        }
        
        // Set data
        SetDataForItem(item, dataIndex, _data[dataIndex]);
        
        // Calculate absolute position within the virtual content, then adjust for container coordinate system
        float itemAbsoluteY = _paddingTop + row * (_itemSize.Y + _spacing.Y);
        
        // Convert to container's coordinate system where (0,0) is center
        // Items at the top of content should have positive Y, items at bottom negative Y
        float currentY = (_totalContentHeight * 0.5f) - itemAbsoluteY;
        
        // X position: for grid layout, calculate column position
        float currentX;
        if (_columnsPerRow == 1)
        {
            // Vertical list: center horizontally or use anchor
            currentX = 0f; // Let anchor handle horizontal positioning
        }
        else
        {
            // Grid: calculate based on column
            float totalItemsWidth = _columnsPerRow * _itemSize.X + (_columnsPerRow - 1) * _spacing.X;
            float startX = -totalItemsWidth * 0.5f + _itemSize.X * 0.5f;
            currentX = startX + col * (_itemSize.X + _spacing.X);
        }
        
        item.Position = new Vector2(currentX, currentY);
        
        _container.AddChild(item);
        _visibleItems[dataIndex] = item;
    }
    
    private void ClearActiveItems()
    {
        foreach (var kvp in _visibleItems)
        {
            _itemPool.Add(kvp.Value);
            _container.RemoveChild(kvp.Value);
        }
        _visibleItems.Clear();
    }
    
    /// <summary>
    /// Binds data to a list item if it implements IUIListItem.
    /// </summary>
    protected static void SetDataForItem(UINode item, int index, TData data)
    {
        if (item is IUIListItem<TData> uiListItem)
        {
            uiListItem.SetData(index, data);
        }
    }
    
    /// <summary>
    /// Internal container that handles position changes and updates the virtual view.
    /// </summary>
    private class VirtualContainer : UINode
    {
        private readonly UIVirtualList<TData> _virtualList;
        private Vector2 _lastPosition;
        
        public VirtualContainer(UIVirtualList<TData> virtualList)
        {
            _virtualList = virtualList;
            _lastPosition = Position;
        }
        
        protected override void OnUpdateRenderData(Canvas canvas, float delta)
        {
            base.OnUpdateRenderData(canvas, delta);
            
            // Check if position changed (scrolling occurred)
            if (_lastPosition != Position)
            {
                _lastPosition = Position;
                _virtualList.UpdateVirtualView();
            }
        }
        
        public void AddChild(UINode child)
        {
            Add(child, false);
        }
        
        public void RemoveChild(UINode child)
        {
            Remove(child);
        }
    }
}