using System.Runtime.CompilerServices;
using Vocore.Graphics;
using Vocore.Rendering;

namespace Vocore.GUI;

public class UIButton : UINode, IUIEventReceiver
{
    private event Action? _onClickEvent;
    private event Action? _onHoverEvent;
    private event Action? _onPressDownEvent;
    private event Action? _onPressUpEvent;
    private event Action? _onPressingEvent;
    private TransitionMode _transitionMode = TransitionMode.None;

    public UISprite? TransitionTarget { get; set; } = null;
    //for TransitionMode.ColorTint, use TransitionTarget as the target
    public ColorFloat ColorNormal { get; set; } = new ColorFloat(1, 1, 1, 1);
    public ColorFloat ColorHover { get; set; } = new ColorFloat(1, 1, 1, 1);
    public ColorFloat ColorPressing { get; set; } = new ColorFloat(1, 1, 1, 1);
    public ColorFloat ColorDisabled { get; set; } = new ColorFloat(1, 1, 1, 1);
    public float FadeDuration { get; set; } = 0.1f;

    //for TransitionMode.SpriteSwap, use TransitionTarget as the target
    public Texture2D? SpriteNormal { get; set; } = null;
    public Texture2D? SpriteHover { get; set; } = null;
    public Texture2D? SpritePressing { get; set; } = null;
    public Texture2D? SpriteDisabled { get; set; } = null;

    //for TransitionMode.NodeSwap, TransitionTarget not used
    public UINode? NodeNormal { get; set; } = null;
    public UINode? NodeHover { get; set; } = null;
    public UINode? NodePressing { get; set; } = null;
    public UINode? NodeDisabled { get; set; } = null;


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

    public TransitionMode TransitionMode
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _transitionMode;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _transitionMode = value;
    }

    public UIButton()
    {
        
    }

    
    protected override void OnUpdate(Canvas canvas, float delta)
    {
        AddSelfForCollision(canvas);
    }

    public void OnClick()
    {
        _onClickEvent?.Invoke();
    }

    public void OnHover()
    {
        _onHoverEvent?.Invoke();
    }

    public void OnPressing()
    {
        _onPressingEvent?.Invoke();
    }

    public void OnPressDown()
    {
        _onPressDownEvent?.Invoke();
    }

    public void OnPressUp()
    {
        _onPressUpEvent?.Invoke();
    }

    private void AddSelfForCollision(Canvas canvas)
    {
        Transform2D transform = RenderTransform;
        ShapeBox2D shape = new ShapeBox2D(transform.position, transform.scale);
        canvas.AddClickReciever(this, shape);
    }

    
}