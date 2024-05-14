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
    private TransitionState _transitionState = TransitionState.Normal;


    public UISprite? TransitionTarget { get; set; } = null;
    private float _tTransition = 0;
    //for TransitionMode.ColorTint, use TransitionTarget as the target

    private ColorFloat _colorTweenStart = new ColorFloat(1, 1, 1, 1);
    private ColorFloat _colorTweenEnd = new ColorFloat(1, 1, 1, 1);
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
        set
        {
            _transitionMode = value;
            OnTransitionStateChanged();
        }
    }

    public UIButton()
    {
        
    }

    
    protected override void OnUpdate(Canvas canvas, float delta)
    {
        AddSelfForCollision(canvas);

        if (_transitionMode == TransitionMode.ColorTint)
        {
            UpdateColorTween(delta);
        }

        if (_transitionState == TransitionState.Hover && canvas.Hovered != this)
        {
            TryChangeTransitionState(TransitionState.Normal);
        }
    }

    public void OnClick()
    {
        _onClickEvent?.Invoke();
    }

    public void OnHover()
    {
        _onHoverEvent?.Invoke();
        TryChangeTransitionState(TransitionState.Hover);
    }

    public void OnPressing()
    {
        _onPressingEvent?.Invoke();
    }

    public void OnPressDown()
    {
        _onPressDownEvent?.Invoke();
        TryChangeTransitionState(TransitionState.Pressing);
    }

    public void OnPressUp()
    {
        _onPressUpEvent?.Invoke();
        TryChangeTransitionState(TransitionState.Normal);
    }

    private void AddSelfForCollision(Canvas canvas)
    {
        Transform2D transform = RenderTransform;
        ShapeBox2D shape = new ShapeBox2D(transform.position, transform.scale);
        canvas.AddClickReciever(this, shape);
    }

    private void TryChangeTransitionState(TransitionState state)
    {
        if (_transitionState != state)
        {
            _transitionState = state;
            OnTransitionStateChanged();
        }
    }

    private void OnTransitionStateChanged()
    {
        switch (_transitionMode)
        {
            case TransitionMode.ColorTint:
                ColorFloat current = ColorFloat.Lerp(_colorTweenStart, _colorTweenEnd, _tTransition);
                StartColorTween(current, GetColorFromState(_transitionState));
                break;
            case TransitionMode.SpriteSwap:
                RefreshSpriteSwap();
                break;
            case TransitionMode.NodeSwap:
                RefreshNodeSwap();
                break;
            default:
                break;
        }
    }

    private void StartColorTween(ColorFloat start, ColorFloat end)
    {
        _colorTweenStart = start;
        _colorTweenEnd = end;
        _tTransition = 0;
    }

    private void UpdateColorTween(float delta)
    {
        _tTransition += delta / FadeDuration;
        _tTransition = math.clamp(_tTransition, 0, 1);
        if (TransitionTarget == null)
        {
            return;
        }
        TransitionTarget.Color = ColorFloat.Lerp(_colorTweenStart, _colorTweenEnd, _tTransition);
    }

    private void RefreshSpriteSwap()
    {
        if (TransitionTarget == null)
        {
            return;
        }

        switch (_transitionState)
        {
            case TransitionState.Normal:
                TransitionTarget.Texture = SpriteNormal;
                break;
            case TransitionState.Hover:
                TransitionTarget.Texture = SpriteHover;
                break;
            case TransitionState.Pressing:
                TransitionTarget.Texture = SpritePressing;
                break;
            case TransitionState.Disabled:
                TransitionTarget.Texture = SpriteDisabled;
                break;
            default:
                break;
        }
    }

    private void RefreshNodeSwap()
    {
        if (NodeNormal != null)
        {
            NodeNormal.IsVisible = _transitionState == TransitionState.Normal;
        }

        if (NodeHover != null)
        {
            NodeHover.IsVisible = _transitionState == TransitionState.Hover;
        }

        if (NodePressing != null)
        {
            NodePressing.IsVisible = _transitionState == TransitionState.Pressing;
        }

        if (NodeDisabled != null)
        {
            NodeDisabled.IsVisible = _transitionState == TransitionState.Disabled;
        }
    }

    private ColorFloat GetColorFromState(TransitionState state)
    {
        switch (state)
        {
            case TransitionState.Normal:
                return ColorNormal;
            case TransitionState.Hover:
                return ColorHover;
            case TransitionState.Pressing:
                return ColorPressing;
            case TransitionState.Disabled:
                return ColorDisabled;
            default:
                return ColorNormal;
        }
    }


}