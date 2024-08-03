using System.Numerics;
using System.Runtime.CompilerServices;

namespace Vocore.GUI;

public class UIScrollable : UISelectable
{
    private UINode? _content;
    private Vector2 _lastDragPosition;
    public SrollMode ScrollMode { get; set; }
    public UINode? Content
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _content;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            if (value == null)
            {
                _content = null;
                return;
            }

            if (value.Parent != this)
            {
                throw new InvalidOperationException("Content must be a child of this node");
            }

            _content = value;
        }
    }


    protected override void OnUpdate(Canvas canvas, float delta)
    {
        base.OnUpdate(canvas, delta);
    }

    public override void OnPressDown(Vector2 mousePosition)
    {
        base.OnPressDown(mousePosition);
        _lastDragPosition = mousePosition;
    }

    public override void OnDrag(Vector2 mousePoisition)
    {
        base.OnDrag(mousePoisition);
        if (_content == null)
        {
            return;
        }

        Vector2 displacement = mousePoisition - _lastDragPosition;
        _lastDragPosition = mousePoisition;
        OnScroll(displacement);
    }

    protected void OnScroll(Vector2 displacement)
    {
        if (_content == null)
        {
            return;
        }

        Vector2 position = _content.Position + displacement;
        SetContentPosition(position);
    }

    private void SetContentPosition(Vector2 position)
    {
        BoundingBox2D bound = Bound;
        Vector2 contentOffset = _content!.Size * _content.Pivot;
        bound.min -= contentOffset;
        bound.max -= contentOffset;

        if ((ScrollMode & SrollMode.Vertical) != 0)
        {
            _content.Position = new Vector2(_content.Position.X, position.Y);

            //clmap
            if (_content.Position.Y < bound.min.Y)
            {
                _content.Position = new Vector2(_content.Position.X, bound.min.Y);
            }
            else if (_content.Position.Y > bound.max.Y)
            {
                _content.Position = new Vector2(_content.Position.X, bound.max.Y);
            }
        }

        if ((ScrollMode & SrollMode.Horizontal) != 0)
        {
            _content.Position = new Vector2(position.X, _content.Position.Y);

            //clmap
            if (_content.Position.X < bound.min.X)
            {
                _content.Position = new Vector2(bound.min.X, _content.Position.Y);
            }
            else if (_content.Position.X > bound.max.X)
            {
                _content.Position = new Vector2(bound.max.X, _content.Position.Y);
            }
        }
    }
}