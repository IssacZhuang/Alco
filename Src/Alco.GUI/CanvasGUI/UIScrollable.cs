using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco.GUI;

public class UIScrollable : UISelectable
{
    private UINode? _content;
    private Vector2 _lastDragPosition;
    private Vector2 _inertiaVelocity;
    private bool _isDragging;
    private float _lastDeltaTime;
    private const float MaxInertiaSpeed = 6000f;
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


    protected override void OnRender(Canvas canvas, float delta)
    {
        base.OnRender(canvas, delta);

        Vector2 contentSize = _content?.Size ?? Vector2.Zero;

    }

    /// <summary>
    /// Inertia strength [0,1]. 0 stops immediately on release; 1 never decelerates.
    /// Velocity retains Inertia per second, thus retains Inertia^delta per frame.
    /// </summary>
    public float Inertia { get; set; } = 0.05f;

    protected override void OnTick(Canvas canvas, float delta)
    {
        base.OnTick(canvas, delta);
        _lastDeltaTime = delta;
        if (_content == null)
        {
            return;
        }

        if (!_isDragging)
        {
            if ((ScrollMode & SrollMode.Vertical) == 0) _inertiaVelocity.Y = 0f;
            if ((ScrollMode & SrollMode.Horizontal) == 0) _inertiaVelocity.X = 0f;

            if (_inertiaVelocity.LengthSquared() > 0.0001f)
            {
                Vector2 before = _content.Position;
                Vector2 displacement = _inertiaVelocity * delta;
                SetContentPosition(before + displacement);
                Vector2 after = _content.Position;
                Vector2 applied = after - before;

                // If clamped by bounds, stop motion along that axis
                if (MathF.Abs(applied.X - displacement.X) > 0.001f) _inertiaVelocity.X = 0f;
                if (MathF.Abs(applied.Y - displacement.Y) > 0.001f) _inertiaVelocity.Y = 0f;

                float inertiaClamped = Math.Clamp(Inertia, 0f, 1f);
                float perFrameRetention = MathF.Pow(inertiaClamped, delta);
                _inertiaVelocity *= perFrameRetention;

                if (inertiaClamped <= 0f || _inertiaVelocity.LengthSquared() < 1f)
                {
                    _inertiaVelocity = Vector2.Zero;
                }
            }
        }
    }

    public override void OnPressDown(Canvas canvas, Vector2 mousePosition)
    {
        base.OnPressDown(canvas, mousePosition);
        _lastDragPosition = mousePosition;
        _isDragging = true;
        _inertiaVelocity = Vector2.Zero;
    }

    public override void OnDrag(Canvas canvas, Vector2 mousePoisition)
    {
        base.OnDrag(canvas, mousePoisition);
        if (_content == null)
        {
            return;
        }

        Vector2 displacement = mousePoisition - _lastDragPosition;
        float dt = _lastDeltaTime > 0.00001f ? _lastDeltaTime : 0.016f;
        Vector2 instantVelocity = displacement / dt;
        // Smooth velocity for stability and better feel
        _inertiaVelocity = Vector2.Lerp(_inertiaVelocity, instantVelocity, 0.5f);
        float speed = _inertiaVelocity.Length();
        if (speed > MaxInertiaSpeed)
        {
            _inertiaVelocity *= MaxInertiaSpeed / speed;
        }
        _lastDragPosition = mousePoisition;
        OnScroll(displacement);
    }

    public override void OnPressUp(Canvas canvas, Vector2 mousePosition)
    {
        base.OnPressUp(canvas, mousePosition);
        _isDragging = false;
        if (Inertia <= 0f)
        {
            _inertiaVelocity = Vector2.Zero;
        }
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
            Min = boundSelf.Min - boundContent.Min,
            Max = boundSelf.Max - boundContent.Max,
        };

        if (boundPosition.Min.X > 0)
        {
            boundPosition.Min.X = 0;
        }

        if (boundPosition.Min.Y > 0)
        {
            boundPosition.Min.Y = 0;
        }

        if (boundPosition.Max.X < 0)
        {
            boundPosition.Max.X = 0;
        }

        if (boundPosition.Max.Y < 0)
        {
            boundPosition.Max.Y = 0;
        }

        if ((ScrollMode & SrollMode.Vertical) != 0)
        {
            _content!.Position = new Vector2(_content.Position.X, position.Y);

            // clamp
            if (_content.Position.Y < boundPosition.Min.Y)
            {
                _content.Position = new Vector2(_content.Position.X, boundPosition.Min.Y);
            }
            else if (_content.Position.Y > boundPosition.Max.Y)
            {
                _content.Position = new Vector2(_content.Position.X, boundPosition.Max.Y);
            }
        }

        if ((ScrollMode & SrollMode.Horizontal) != 0)
        {
            _content!.Position = new Vector2(position.X, _content.Position.Y);

            // clamp
            if (_content.Position.X < boundPosition.Min.X)
            {
                _content.Position = new Vector2(boundPosition.Min.X, _content.Position.Y);
            }
            else if (_content.Position.X > boundPosition.Max.X)
            {
                _content.Position = new Vector2(boundPosition.Max.X, _content.Position.Y);
            }
        }
    }

    private static BoundingBox2D GetLocalBound(UINode node)
    {
        Transform2D transform = node.RenderTransform;
        Vector2 halfSize = transform.Scale * 0.5f;
        return new BoundingBox2D(halfSize, -halfSize);
    }
}