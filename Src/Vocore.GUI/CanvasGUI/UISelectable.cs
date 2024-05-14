using System.Runtime.CompilerServices;

namespace Vocore.GUI;

public class UISelectable : UINode, IUIEventReceiver
{
    private event Action? _onClickEvent;
    private event Action? _onHoverEvent;
    private event Action? _onPressDownEvent;
    private event Action? _onPressUpEvent;
    private event Action? _onPressingEvent;

    #region  Event
    public event Action OnClickEvent
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        add => _onClickEvent += value;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        remove => _onClickEvent -= value;
    }

    public event Action OnHoverEvent
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        add => _onHoverEvent += value;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        remove => _onHoverEvent -= value;
    }

    public event Action OnPressDownEvent
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        add => _onPressDownEvent += value;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        remove => _onPressDownEvent -= value;
    }

    public event Action OnPressUpEvent
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        add => _onPressUpEvent += value;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        remove => _onPressUpEvent -= value;
    }

    public event Action OnPressingEvent
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        add => _onPressingEvent += value;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        remove => _onPressingEvent -= value;
    }

    #endregion

    protected override void OnUpdate(Canvas canvas, float delta)
    {
        AddSelfForCollision(canvas);
    }

    public virtual void OnClick()
    {
        _onClickEvent?.Invoke();
    }

    public virtual void OnHover()
    {
        _onHoverEvent?.Invoke();
    }

    public virtual void OnPressing()
    {
        _onPressingEvent?.Invoke();
    }

    public virtual void OnPressDown()
    {
        _onPressDownEvent?.Invoke();
    }

    public virtual void OnPressUp()
    {
        _onPressUpEvent?.Invoke();
    }

    protected void AddSelfForCollision(Canvas canvas)
    {
        Transform2D transform = RenderTransform;
        ShapeBox2D shape = new ShapeBox2D(transform.position, transform.scale);
        canvas.AddClickReciever(this, shape);
    }
}