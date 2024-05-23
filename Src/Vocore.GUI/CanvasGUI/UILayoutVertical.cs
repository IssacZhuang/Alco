using System.Drawing;
using System.Numerics;

namespace Vocore.GUI;

public class UILayoutVertical : UINode
{
    private bool _isFixedHeight;
    private bool _alwaysUpdate; // if true, the layout will update every frame
    private bool _fitContentHeight;
    private float _paddingTop;
    private float _paddingBottom;
    private float _spacing;
    private float _fixedHeight; // only used if _isFixedHeight is true

    public float PaddingTop
    {
        get => _paddingTop;
        set
        {
            _paddingTop = value;
        }
    }

    public float PaddingBottom
    {
        get => _paddingBottom;
        set
        {
            _paddingBottom = value;
        }
    }


    public float FixedHeight
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

    public float Spacing
    {
        get => _spacing;
        set
        {
            _spacing = value;
        }
    }

    public bool IsFixedHeight
    {
        get => _isFixedHeight;
        set
        {
            _isFixedHeight = value;
        }
    }

    public bool FitContentHeight
    {
        get => _fitContentHeight;
        set
        {
            _fitContentHeight = value;
        }
    }

    public UILayoutVertical()
    {

    }

    protected override void OnUpdate(Canvas canvas, float delta)
    {
        base.OnUpdate(canvas, delta);
        if (_alwaysUpdate)
        {
            UpdateLayout();
        }
    }

    public void UpdateLayout()
    {
        if (_fitContentHeight)
        {
            float height = _paddingTop + _paddingBottom;
            bool hasElement = false;
            for (int i = 0; i < Children.Count; i++)
            {
                UINode child = Children[i];
                if (child.IsLayoutAffected)
                {
                    hasElement = true;
                    if (_isFixedHeight)
                    {
                        height += _fixedHeight + _spacing;
                    }
                    else
                    {
                        height += child.Size.Y + _spacing;
                    }
                }
            }

            if (hasElement)
            {
                height -= _spacing;//last one has no spacing
            }

            Size = new Vector2(Size.X, height);
        }


        float currentY = Size.Y * 0.5f;
        currentY -= _paddingTop;
        for (int i = 0; i < Children.Count; i++)
        {
            UINode child = Children[i];

            if (!child.IsLayoutAffected)
            {
                continue;
            }

            child.Pivot = new Pivot(child.Pivot.X, -0.5f);
            if (_isFixedHeight)
            {
                child.Position = new Vector2(child.Position.X, currentY);
                currentY -= _fixedHeight + _spacing;
            }
            else
            {
                child.Position = new Vector2(child.Position.X, currentY);
                currentY -= child.Size.Y + _spacing;
            }
        }
    }
}