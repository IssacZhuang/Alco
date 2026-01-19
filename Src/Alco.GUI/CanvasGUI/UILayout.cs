using System.Drawing;
using System.Numerics;
using Alco;

namespace Alco.GUI;

/// <summary>
/// Layout arrangement types
/// </summary>
public enum LayoutType
{
    /// <summary>
    /// Vertical layout - items arranged from top to bottom
    /// </summary>
    Vertical,
    
    /// <summary>
    /// Horizontal layout - items arranged from left to right
    /// </summary>
    Horizontal,
    
    /// <summary>
    /// Grid layout - items arranged in a grid pattern
    /// </summary>
    Grid
}

/// <summary>
/// A flexible layout container that supports vertical, horizontal, and grid arrangements
/// </summary>
public class UILayout : UINode
{
    private LayoutType _layoutType = LayoutType.Vertical;
    private bool _isFixedSize;
    private bool _alwaysUpdate; // if true, the layout will update every frame
    private bool _fitContentSize;
    private bool _skipDisabledItem = true;
    private Padding _padding;
    private Vector2 _spacing;
    private float _fixedWidth; // only used if _isFixedSize is true
    private float _fixedHeight; // only used if _isFixedSize is true
    private bool _isDirty;



    /// <summary>
    /// Layout arrangement type
    /// </summary>
    public LayoutType LayoutType
    {
        get => _layoutType;
        set
        {
            _layoutType = value;
        }
    }



    /// <summary>
    /// Gets or sets padding for all four sides.
    /// </summary>
    public Padding Padding
    {
        get => _padding;
        set => _padding = value;
    }

    public float FixedItemWidth
    {
        get => _fixedWidth;
        set
        {
            _fixedWidth = value;
        }
    }

    public float FixedItemHeight
    {
        get => _fixedHeight;
        set
        {
            _fixedHeight = value;
        }
    }


    public bool AlwaysUpdate
    {
        get => _alwaysUpdate;
        set
        {
            _alwaysUpdate = value;
        }
    }

    /// <summary>
    /// Spacing between items (X: horizontal spacing, Y: vertical spacing)
    /// </summary>
    public Vector2 Spacing
    {
        get => _spacing;
        set
        {
            _spacing = value;
        }
    }

    /// <summary>
    /// Whether to use fixed size for all child elements
    /// </summary>
    public bool IsFixedItemSize
    {
        get => _isFixedSize;
        set
        {
            _isFixedSize = value;
        }
    }

    /// <summary>
    /// Whether to fit content size automatically
    /// </summary>
    public bool FitContentSize
    {
        get => _fitContentSize;
        set
        {
            _fitContentSize = value;
        }
    }

    /// <summary>
    /// Whether to skip disabled items when calculating layout
    /// </summary>
    public bool SkipDisabledItem
    {
        get => _skipDisabledItem;
        set
        {
            _skipDisabledItem = value;
        }
    }

    /// <summary>
    /// Legacy property for backward compatibility
    /// </summary>
    public bool IsFixedHeight
    {
        get => _isFixedSize;
        set => _isFixedSize = value;
    }

    /// <summary>
    /// Creates a new UILayout with vertical arrangement by default
    /// </summary>
    public UILayout()
    {

    }

    /// <summary>
    /// Adds a child node and automatically sets appropriate anchor based on layout type
    /// </summary>
    public override void Add(UINode node, bool keepWorldTransform = false)
    {
        // Set appropriate anchor based on layout type
        if(ShouldIncludeInLayout(node))
        {
            SetChildAnchor(node);
        }
        
        // Call base implementation
        base.Add(node, keepWorldTransform);
        _isDirty = true;
    }

    /// <summary>
    /// Sets appropriate anchor for child node based on layout type
    /// </summary>
    private void SetChildAnchor(UINode child)
    {
        // All layout types use Anchor.Center for consistent calculation
        // child.Anchor = Anchor.Center;
        switch (_layoutType)
        {
            case LayoutType.Vertical:
                child.Anchor = Anchor.DestretchVertical(child.Anchor);
                break;
            case LayoutType.Horizontal:
                child.Anchor = Anchor.DestretchHorizontal(child.Anchor);
                break;
            case LayoutType.Grid:
                child.Anchor = Anchor.Center;
                break;
        }
    }

    protected override void OnUpdate(Canvas canvas, float delta)
    {
        if (_alwaysUpdate || _isDirty)
        {
            UpdateLayout();
            _isDirty = false;
        }
        base.OnUpdate(canvas, delta);
    }

    /// <summary>
    /// Updates the layout arrangement of all child elements
    /// </summary>
    public void UpdateLayout()
    {
        // Update anchors for all children in case layout type changed
        
        switch (_layoutType)
        {
            case LayoutType.Vertical:
                UpdateVerticalLayout();
                break;
            case LayoutType.Horizontal:
                UpdateHorizontalLayout();
                break;
            case LayoutType.Grid:
                UpdateGridLayout();
                break;
        }
    }

    /// <summary>
    /// Checks if a child should be included in the layout calculation
    /// </summary>
    private bool ShouldIncludeInLayout(UINode child)
    {
        if (!child.IsLayoutAffected)
            return false;

        if (_skipDisabledItem && !child.IsEnable)
            return false;

        return true;
    }

    private void UpdateVerticalLayout()
    {
        if (_fitContentSize)
        {
            float height = _padding.Vertical;
            bool hasElement = false;
            for (int i = 0; i < Children.Count; i++)
            {
                UINode child = Children[i];
                if (ShouldIncludeInLayout(child))
                {
                    hasElement = true;
                    if (_isFixedSize)
                    {
                        height += _fixedHeight + _spacing.Y;
                    }
                    else
                    {
                        height += child.RenderSize.Y + _spacing.Y;
                    }
                }
            }

            if (hasElement)
            {
                height -= _spacing.Y;
            }

            Size = new Vector2(Size.X, height);
        }

        float currentY = Size.Y * 0.5f;
        currentY -= _padding.Top;
        for (int i = 0; i < Children.Count; i++)
        {
            UINode child = Children[i];

            if (!ShouldIncludeInLayout(child))
            {
                continue;
            }

            SetChildAnchor(child);

            child.Pivot = new Pivot(child.Pivot.X, 0f);
            if (_isFixedSize)
            {
                child.Position = new Vector2(child.Position.X, currentY - _fixedHeight * 0.5f);
                currentY -= _fixedHeight + _spacing.Y;
            }
            else
            {
                child.Position = new Vector2(child.Position.X, currentY - child.RenderSize.Y * 0.5f);
                currentY -= child.RenderSize.Y + _spacing.Y;
            }
        }
    }

    private void UpdateHorizontalLayout()
    {
        if (_fitContentSize)
        {
            float width = _padding.Horizontal;
            bool hasElement = false;
            for (int i = 0; i < Children.Count; i++)
            {
                UINode child = Children[i];
                if (ShouldIncludeInLayout(child))
                {
                    hasElement = true;
                    if (_isFixedSize)
                    {
                        width += _fixedWidth + _spacing.X;
                    }
                    else
                    {
                        width += child.RenderSize.X + _spacing.X;
                    }
                }
            }

            if (hasElement)
            {
                width -= _spacing.X;
            }

            Size = new Vector2(width, Size.Y);
        }

        float startX = -Size.X * 0.5f + _padding.Left;
        float currentX = startX;
        
        for (int i = 0; i < Children.Count; i++)
        {
            UINode child = Children[i];

            if (!ShouldIncludeInLayout(child))
            {
                continue;
            }

            // Set pivot to center for consistent positioning
            child.Pivot = new Pivot(0f, child.Pivot.Y);

            SetChildAnchor(child);

            if (_isFixedSize)
            {
                // Position at the center of the item area
                child.Position = new Vector2(currentX + _fixedWidth * 0.5f, child.Position.Y);
                currentX += _fixedWidth + _spacing.X;
            }
            else
            {
                // Position at the center of the item area  
                child.Position = new Vector2(currentX + child.RenderSize.X * 0.5f, child.Position.Y);
                currentX += child.RenderSize.X + _spacing.X;
            }
        }
    }

    private void UpdateGridLayout()
    {
        var affectedChildren = new List<UINode>();
        for (int i = 0; i < Children.Count; i++)
        {
            if (ShouldIncludeInLayout(Children[i]))
            {
                affectedChildren.Add(Children[i]);
            }
        }

        if (affectedChildren.Count == 0)
            return;

        // Calculate item size
        float itemWidth, itemHeight;
        if (_isFixedSize)
        {
            itemWidth = _fixedWidth;
            itemHeight = _fixedHeight;
        }
        else
        {
            // Use the first child's size as reference for all items
            itemWidth = affectedChildren[0].RenderSize.X;
            itemHeight = affectedChildren[0].RenderSize.Y;
        }

        // Calculate how many columns can fit based on available width
        float availableWidth = Size.X - _padding.Horizontal;
        int columnsPerRow = Math.Max(1, (int)((availableWidth + _spacing.X) / (itemWidth + _spacing.X)));
        
        // Calculate number of rows needed
        int totalRows = (int)Math.Ceiling((float)affectedChildren.Count / columnsPerRow);

        // Auto-fit content size if enabled (fit height only, keep width unchanged)
        if (_fitContentSize)
        {
            float totalHeight = _padding.Vertical + totalRows * itemHeight + (totalRows - 1) * _spacing.Y;
            Size = new Vector2(Size.X, totalHeight);
        }

        // Position items
        float startX = -Size.X * 0.5f + _padding.Left;
        float startY = Size.Y * 0.5f - _padding.Top;

        for (int i = 0; i < affectedChildren.Count; i++)
        {
            UINode child = affectedChildren[i];

            if (!ShouldIncludeInLayout(child))
            {
                continue;
            }

            SetChildAnchor(child);

            int col = i % columnsPerRow;
            int row = i / columnsPerRow;

            float x = startX + col * (itemWidth + _spacing.X) + itemWidth * 0.5f;
            float y = startY - row * (itemHeight + _spacing.Y) - itemHeight * 0.5f;

            child.Pivot = new Pivot(0f, 0f); // Center pivot
            child.Position = new Vector2(x, y);
            
            // Ensure consistent size for grid items
            if (_isFixedSize)
            {
                child.Size = new Vector2(_fixedWidth, _fixedHeight);
            }
        }
    }
}