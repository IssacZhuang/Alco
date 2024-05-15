using System.Runtime.CompilerServices;

namespace Vocore.GUI;

/// <summary>
/// The UI node that can be received the UI event.
/// </summary>
public class UISelectable : UINode, IUIEventReceiver
{
    private event Action? _onClickEvent;
    private event Action? _onHoverEvent;
    private event Action? _onPressDownEvent;
    private event Action? _onPressUpEvent;
    private event Action? _onPressingEvent;

    #region  Event
    /// <summary>
    /// Called when the UI node is clicked.
    /// </summary>
    public event Action OnClickEvent
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        add => _onClickEvent += value;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        remove => _onClickEvent -= value;
    }

    /// <summary>
    /// Called when the UI node is hovered.
    /// </summary>
    public event Action OnHoverEvent
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        add => _onHoverEvent += value;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        remove => _onHoverEvent -= value;
    }

    /// <summary>
    /// Called when the UI node is pressed down.
    /// </summary>
    public event Action OnPressDownEvent
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        add => _onPressDownEvent += value;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        remove => _onPressDownEvent -= value;
    }

    /// <summary>
    /// Called when the UI node is pressed up.
    /// </summary>
    public event Action OnPressUpEvent
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        add => _onPressUpEvent += value;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        remove => _onPressUpEvent -= value;
    }

    /// <summary>
    /// Called when the UI node is pressing.
    /// </summary>
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

    /// <summary>
    /// Called when the UI node is clicked.
    /// </summary>
    public virtual void OnClick()
    {
        _onClickEvent?.Invoke();
    }

    /// <summary>
    /// Called when the UI node is hovered.
    /// </summary>
    public virtual void OnHover()
    {
        _onHoverEvent?.Invoke();
    }

    /// <summary>
    /// Called when the UI node is pressing.
    /// </summary>
    public virtual void OnPressing()
    {
        _onPressingEvent?.Invoke();
    }

    /// <summary>
    /// Called when the UI node is pressed down.
    /// </summary>
    public virtual void OnPressDown()
    {
        _onPressDownEvent?.Invoke();
    }

    /// <summary>
    /// Called when the UI node is pressed up.
    /// </summary>
    public virtual void OnPressUp()
    {
        _onPressUpEvent?.Invoke();
    }

    /// <summary>
    /// Add self for collision.
    /// </summary>
    /// <param name="canvas">The canvas that handle the collision world.</param>
    protected void AddSelfForCollision(Canvas canvas)
    {
        Transform2D transform = RenderTransform;
        ShapeBox2D shape = new ShapeBox2D(transform.position, transform.scale);
        canvas.AddClickReciever(this, shape);
    }
}