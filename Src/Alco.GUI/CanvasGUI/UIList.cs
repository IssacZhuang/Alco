namespace Alco.GUI;

/// <summary>
/// A vertical list container that arranges items and supports scrolling.
/// It internally uses a <see cref="UIMask"/> + <see cref="UIScrollable"/> + <see cref="UILayoutVertical"/>
/// to layout and clip the content. Call <see cref="SetItems(System.Collections.Generic.IReadOnlyList{TData})"/>
/// to populate the list and reuse pooled item nodes.
/// </summary>
public abstract class UIList<TData> : UINode
{
    // used for virtual list/pooling
    private readonly List<UINode> _pool = new();
    private readonly List<TData> _data = new();

    private readonly UIMask _mask;
    private readonly UIScrollable _scrollable;
    private readonly UILayoutVertical _layout;

    /// <summary>
    /// Gets the internal vertical layout container.
    /// </summary>
    public UILayoutVertical Layout => _layout;

    /// <summary>
    /// Gets or sets the scroll mode for the list viewport.
    /// </summary>
    public SrollMode ScrollMode
    {
        get => _scrollable.ScrollMode;
        set => _scrollable.ScrollMode = value;
    }

    /// <summary>
    /// Top padding applied by the internal vertical layout.
    /// </summary>
    public float PaddingTop
    {
        get => _layout.PaddingTop;
        set => _layout.PaddingTop = value;
    }

    /// <summary>
    /// Bottom padding applied by the internal vertical layout.
    /// </summary>
    public float PaddingBottom
    {
        get => _layout.PaddingBottom;
        set => _layout.PaddingBottom = value;
    }

    /// <summary>
    /// Spacing between items in the internal vertical layout.
    /// </summary>
    public float Spacing
    {
        get => _layout.Spacing;
        set => _layout.Spacing = value;
    }

    /// <summary>
    /// Whether the internal vertical layout uses a fixed height per item.
    /// </summary>
    public bool IsFixedItemHeight
    {
        get => _layout.IsFixedHeight;
        set => _layout.IsFixedHeight = value;
    }

    /// <summary>
    /// Fixed item height used when <see cref="IsFixedItemHeight"/> is true.
    /// </summary>
    public float FixedItemHeight
    {
        get => _layout.FixedHeight;
        set => _layout.FixedHeight = value;
    }

    /// <summary>
    /// Current number of data items bound to the list.
    /// </summary>
    public int Count => _data.Count;

    protected UIList()
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

        _layout = new UILayoutVertical
        {
            Anchor = Anchor.Stretch,
            FitContentHeight = true,
            IsFixedHeight = false,
            Spacing = 4f
        };

        // Wire up hierarchy: this -> mask -> scrollable -> layout
        _mask.Add(_scrollable, false);
        _scrollable.Add(_layout, false);
        _scrollable.Content = _layout;
        Add(_mask, false);
    }

    /// <summary>
    /// Factory method to create a new item node. Implementors should return a configured child node
    /// which optionally implements <see cref="IUIListItem{TData}"/> to receive data binding.
    /// </summary>
    /// <returns>The created item node.</returns>
    protected abstract UINode CreateItem();

    /// <summary>
    /// Sets/refreshes the items displayed by this list and reuses pooled nodes where possible.
    /// </summary>
    /// <param name="items">The items to display.</param>
    public void SetItems(IReadOnlyList<TData> items)
    {
        // ensure pool capacity
        for (int i = _pool.Count; i < items.Count; i++)
        {
            UINode item = CreateItem();
            _pool.Add(item);
            _layout.Add(item, false);
        }

        // bind and enable required items
        for (int i = 0; i < items.Count; i++)
        {
            UINode item = _pool[i];
            item.IsEnable = true;
            item.IsLayoutAffected = true;
            SetDataForItem(item, i, items[i]);
        }

        // disable extras
        for (int i = items.Count; i < _pool.Count; i++)
        {
            UINode item = _pool[i];
            item.IsEnable = false;
            item.IsLayoutAffected = false;
        }

        // copy data snapshot
        _data.Clear();
        for (int i = 0; i < items.Count; i++)
        {
            _data.Add(items[i]);
        }

        // update layout sizing and positions
        _layout.UpdateLayout();
    }

    /// <summary>
    /// Updates data for a single item at the specified index, if it exists in the current pool.
    /// </summary>
    /// <param name="index">Item index.</param>
    /// <param name="data">New data.</param>
    public void SetItem(int index, TData data)
    {
        if ((uint)index >= (uint)_data.Count)
        {
            return;
        }
        _data[index] = data;
        SetDataForItem(_pool[index], index, data);
    }

    /// <summary>
    /// Provides data to a list item if it implements <see cref="IUIListItem{TData}"/>.
    /// </summary>
    /// <param name="item">The item node.</param>
    /// <param name="index">The item index.</param>
    /// <param name="data">The data.</param>
    protected static void SetDataForItem(UINode item, int index, TData data)
    {
        if (item is IUIListItem<TData> uiListItem)
        {
            uiListItem.SetData(index, data);
        }
    }
}