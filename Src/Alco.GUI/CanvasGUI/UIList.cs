using System;
using System.Numerics;

namespace Alco.GUI;

/// <summary>
/// A vertical list container that arranges items and supports scrolling.
/// It internally uses a <see cref="UIMask"/> + <see cref="UIScrollable"/> + <see cref="UILayout"/>
/// to layout and clip the content. Call <see cref="SetItems(System.Collections.Generic.IReadOnlyList{TData})"/>
/// to populate the list and reuse pooled item nodes.
/// </summary>
public abstract class UIList<TData> : UINode
{
    private readonly List<TData> _data = new();

    private readonly UIMask _mask;
    private readonly UIScrollable _scrollable;
    private readonly UILayout _layout;

    /// <summary>
    /// Gets the internal layout container.
    /// </summary>
    public UILayout Layout => _layout;

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
    /// Spacing between items in the internal layout.
    /// </summary>
    public Vector2 Spacing
    {
        get => _layout.Spacing;
        set => _layout.Spacing = value;
    }

    /// <summary>
    /// Legacy spacing property - sets both X and Y spacing values
    /// </summary>
    public float SpacingValue
    {
        get => _layout.SpacingValue;
        set => _layout.SpacingValue = value;
    }

    /// <summary>
    /// Whether the internal layout uses a fixed height per item.
    /// </summary>
    public bool IsFixedItemSize
    {
        get => _layout.IsFixedItemSize;
        set => _layout.IsFixedItemSize = value;
    }

    /// <summary>
    /// Fixed item height used when <see cref="IsFixedItemSize"/> is true.
    /// </summary>
    public float FixedItemHeight
    {
        get => _layout.FixedItemHeight;
        set => _layout.FixedItemHeight = value;
    }

    /// <summary>
    /// Fixed item width used when <see cref="IsFixedItemSize"/> is true.
    /// </summary>
    public float FixedItemWidth
    {
        get => _layout.FixedItemWidth;
        set => _layout.FixedItemWidth = value;
    }

    /// <summary>
    /// Inertia strength [0,1]. 0 stops immediately on release; 1 never decelerates.
    /// Velocity retains Inertia per second, thus retains Inertia^delta per frame.
    /// </summary>
    public float Inertia
    {
        get => _scrollable.Inertia;
        set => _scrollable.Inertia = value;
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

        _layout = new UILayout(LayoutType.Vertical)
        {
            Anchor = Anchor.Stretch,
            FitContentSize = true,
            IsFixedItemSize = false,
            SpacingValue = 4f
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
        // snapshot data first
        _data.Clear();
        for (int i = 0; i < items.Count; i++)
        {
            _data.Add(items[i]);
        }

        ApplyItemsFromData();
    }

    /// <summary>
    /// Sets/refreshes the items using a ReadOnlySpan to avoid intermediate allocations.
    /// </summary>
    /// <param name="items">The span of items to display.</param>
    public void SetItems(ReadOnlySpan<TData> items)
    {
        // snapshot data first
        _data.Clear();
        for (int i = 0; i < items.Length; i++)
        {
            _data.Add(items[i]);
        }

        ApplyItemsFromData();
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
        if ((uint)index >= (uint)_layout.Children.Count)
        {
            return;
        }
        _data[index] = data;
        SetDataForItem(_layout.Children[index], index, data);
    }

    /// <summary>
    /// Rebinds all current items by calling SetData on the existing nodes with stored data.
    /// Useful when visual appearance depends on external state (e.g., selection, theme).
    /// </summary>
    public void RefreshItems()
    {
        int count = Math.Min(_data.Count, _layout.Children.Count);
        for (int i = 0; i < count; i++)
        {
            UINode item = _layout.Children[i];
            SetDataForItem(item, i, _data[i]);
        }
    }

    private void ApplyItemsFromData()
    {
        int desiredCount = _data.Count;
        int currentCount = _layout.Children.Count;

        // remove extras from end so GC can collect
        for (int i = currentCount - 1; i >= desiredCount; i--)
        {
            UINode child = _layout.Children[i];
            _layout.Remove(child);
        }

        // create missing items
        for (int i = _layout.Children.Count; i < desiredCount; i++)
        {
            UINode item = CreateItem();
            _layout.Add(item, false);
        }

        // bind
        for (int i = 0; i < desiredCount; i++)
        {
            UINode item = _layout.Children[i];
            item.IsEnable = true;
            item.IsLayoutAffected = true;
            SetDataForItem(item, i, _data[i]);
        }

        _layout.UpdateLayout();
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