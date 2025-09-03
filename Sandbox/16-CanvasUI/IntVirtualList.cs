using System.Numerics;
using Alco;
using Alco.GUI;
using Alco.Rendering;

/// <summary>
/// A high-performance virtual list for integers that can handle millions of items.
/// Uses the same styling as IntList but only renders visible items.
/// </summary>
public class IntVirtualList : UIVirtualList<int>
{
    /// <summary>
    /// Optional font to apply to each created list item.
    /// </summary>
    public Font? ItemFont { get; set; }

    /// <summary>
    /// Creates a new IntVirtualList with sensible defaults for item spacing and height.
    /// </summary>
    public IntVirtualList()
    {
        Anchor = Anchor.Stretch;
        ColumnsPerRow = 1; // Vertical list (single column)
        ItemSize = new Vector2(100, 28); // Same size as IntListItem + spacing
        Spacing = new Vector2(0f, 4f); // 4px vertical spacing like IntList
    }

    protected override UINode CreateItem()
    {
        var item = new IntVirtualListItem();
        if (ItemFont != null)
        {
            item.LabelFont = ItemFont;
        }
        return item;
    }
}

/// <summary>
/// A virtual list item for integers. Similar to IntListItem but optimized for virtual rendering.
/// </summary>
public class IntVirtualListItem : UIButton, IUIListItem<int>
{
    private readonly UISprite _background;
    private readonly UIText _label;

    /// <summary>
    /// Creates a new integer virtual list item with a background and centered text.
    /// </summary>
    public IntVirtualListItem()
    {
        // Use center anchor and pivot for consistent layout calculation
        Anchor = Anchor.Center;
        Pivot = Pivot.Center;
        Size = new Vector2(100, 24);

        _background = new UISprite
        {
            Anchor = Anchor.Stretch,
            Color = 0x2a2a2a,
            SizeDelta = Vector2.Zero
        };

        _label = new UIText
        {
            Anchor = Anchor.Stretch,
            AlignHorizontal = TextAlign.Center,
            AlignVertical = TextAlign.Center,
            Color = 0xffffff,
            FontSize = 16,
            SizeDelta = Vector2.Zero
        };

        TransitionMode = TransitionMode.ColorTint;
        TransitionTarget = _background;
        ColorNormal = 0x2a2a2a;
        ColorHover = 0x3a3a3a;
        ColorPressing = 0x234A6C;

        Add(_background, false);
        Add(_label, false);
    }

    /// <summary>
    /// Sets or gets the label font of this item.
    /// </summary>
    public Font? LabelFont
    {
        get => _label.Font;
        set => _label.Font = value;
    }

    /// <summary>
    /// Binds the integer data to this item by setting the label text.
    /// </summary>
    /// <param name="index">The item index.</param>
    /// <param name="data">The integer data.</param>
    public void SetData(int index, int data)
    {
        FixedString32 text = new FixedString32();
        text.Append(data);
        _label.SetText(text);
    }
}