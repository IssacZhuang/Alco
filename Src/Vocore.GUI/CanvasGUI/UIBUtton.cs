using System.Runtime.CompilerServices;
using Vocore.Graphics;
using Vocore.Rendering;

namespace Vocore.GUI;

public class UIButton : UISelectable
{


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
        base.OnUpdate(canvas, delta);

        if ((_transitionMode & TransitionMode.ColorTint) != 0)
        {
            UpdateColorTween(delta);
        }

        if (_transitionState == TransitionState.Hover && canvas.Hovered != this)
        {
            TryChangeTransitionState(TransitionState.Normal);
        }
    }

    public override void OnClick()
    {
        base.OnClick();
    }

    public override void OnHover()
    {
        base.OnHover();
        TryChangeTransitionState(TransitionState.Hover);
    }

    public override void OnPressing()
    {
        base.OnPressing();
    }

    public override void OnPressDown()
    {
        base.OnPressDown();
        TryChangeTransitionState(TransitionState.Pressing);
    }

    public override void OnPressUp()
    {
        base.OnPressUp();
        TryChangeTransitionState(TransitionState.Normal);
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
        // switch (_transitionMode)
        // {
        //     case TransitionMode.ColorTint:
        //         ColorFloat current = ColorFloat.Lerp(_colorTweenStart, _colorTweenEnd, _tTransition);
        //         StartColorTween(current, GetColorFromState(_transitionState));
        //         break;
        //     case TransitionMode.SpriteSwap:
        //         RefreshSpriteSwap();
        //         break;
        //     case TransitionMode.NodeSwap:
        //         RefreshNodeSwap();
        //         break;
        //     default:
        //         break;
        // }
        //use flag
        if ((_transitionMode & TransitionMode.ColorTint) != 0)
        {
            ColorFloat current = ColorFloat.Lerp(_colorTweenStart, _colorTweenEnd, _tTransition);
            StartColorTween(current, GetColorFromState(_transitionState));
        }

        if ((_transitionMode & TransitionMode.SpriteSwap) != 0)
        {
            RefreshSpriteSwap();
        }

        if ((_transitionMode & TransitionMode.NodeSwap) != 0)
        {
            RefreshNodeSwap();
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