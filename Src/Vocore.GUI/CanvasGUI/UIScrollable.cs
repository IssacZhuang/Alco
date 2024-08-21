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

        Vector2 contentSize = _content?.Size ?? Vector2.Zero;

        // DebugGUI.Text(Size.ToString());
        // DebugGUI.Text(contentSize.ToString());
        // DebugGUI.Text(_content!.Position.ToString());
    }

    public override void OnPressDown(Canvas canvas, Vector2 mousePosition)
    {
        base.OnPressDown(canvas, mousePosition);
        _lastDragPosition = mousePosition;
    }

    public override void OnDrag(Canvas canvas, Vector2 mousePoisition)
    {
        base.OnDrag(canvas, mousePoisition);
        if (_content == null)
        {
            return;
        }

        Vector2 displacement = mousePoisition -_lastDragPosition;
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
        BoundingBox2D boundSelf = GetLocalBound(this);
        BoundingBox2D boundContent = GetLocalBound(_content!);
        BoundingBox2D boundPosition = new BoundingBox2D()
        {
            min = boundSelf.min - boundContent.min,
            max = boundSelf.max - boundContent.max,
        };

        if (boundPosition.min.X > 0)
        {
            boundPosition.min.X = 0;
        }

        if (boundPosition.min.Y > 0)
        {
            boundPosition.min.Y = 0;
        }

        if (boundPosition.max.X < 0)
        {
            boundPosition.max.X = 0;
        }

        if (boundPosition.max.Y < 0)
        {
            boundPosition.max.Y = 0;
        }

        // DebugGUI.Text(boundSelf.ToString());
        // DebugGUI.Text(boundContent.ToString());
        // DebugGUI.Text(boundPosition.ToString());

        if ((ScrollMode & SrollMode.Vertical) != 0)
        {
            _content!.Position = new Vector2(_content.Position.X, position.Y);

            //clmap
            if (_content.Position.Y < boundPosition.min.Y)
            {
                _content.Position = new Vector2(_content.Position.X, boundPosition.min.Y);
            }
            else if (_content.Position.Y > boundPosition.max.Y)
            {
                _content.Position = new Vector2(_content.Position.X, boundPosition.max.Y);
            }
        }

        if ((ScrollMode & SrollMode.Horizontal) != 0)
        {
            _content!.Position = new Vector2(position.X, _content.Position.Y);

            //clmap
            if (_content.Position.X < boundPosition.min.X)
            {
                _content.Position = new Vector2(boundPosition.min.X, _content.Position.Y);
            }
            else if (_content.Position.X > boundPosition.max.X)
            {
                _content.Position = new Vector2(boundPosition.max.X, _content.Position.Y);
            }
        }
    }

    private static BoundingBox2D GetLocalBound(UINode node)
    {
        Transform2D transform = node.RenderTransform;
        Vector2 halfSize = transform.scale * 0.5f;
        return new BoundingBox2D(halfSize, -halfSize);
    }
}