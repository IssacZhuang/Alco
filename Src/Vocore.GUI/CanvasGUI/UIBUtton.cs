using System.Numerics;
using System.Runtime.CompilerServices;
using Vocore.Graphics;
using Vocore.Rendering;

namespace Vocore.GUI;

/// <summary>
/// The button UI node.
/// </summary>
public class UIButton : UISelectable
{
    private TransitionMode _transitionMode = TransitionMode.None;
    private SelectableState _selectableState = SelectableState.Normal;

    private UINode? _transitionTarget = null;
    private UISprite? _transitionSpriteTarget = null;

    /// <summary>
    /// The target of the transition, depends on the TransitionMode.
    /// The target must be a UISprite if TransitionMode is ColorTint or SpriteSwap
    /// </summary>
    /// <value></value>
    public UINode? TransitionTarget
    {
        get
        {
            return _transitionTarget;
        }
        set
        {
            _transitionTarget = value;
            _transitionSpriteTarget = value as UISprite;
        }
    }
    private float _tTransition = 0;
    private bool _inTransition = false;
    /// <summary>
    /// The duration of the transition
    /// </summary>
    /// <value></value>
    public float FadeDuration { get; set; } = 0.1f;
    //for TransitionMode.ColorTint, use TransitionTarget as the target

    private ColorFloat _colorTweenStart = new ColorFloat(1, 1, 1, 1);
    private ColorFloat _colorTweenEnd = new ColorFloat(1, 1, 1, 1);
    private ColorFloat _colorNormal = new ColorFloat(1, 1, 1, 1);


    /// <summary>
    /// The color of the button in normal state
    /// </summary>
    /// <returns></returns>
    public ColorFloat ColorNormal
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _colorNormal;
        set
        {
            _colorNormal = value;
            _colorTweenStart = value;
            _colorTweenEnd = value;
        }
    }
    /// <summary>
    /// The color of the button in hover state
    /// </summary>
    /// <returns></returns>
    public ColorFloat ColorHover { get; set; } = new ColorFloat(1, 1, 1, 1);
    /// <summary>
    /// The color of the button in pressing state
    /// </summary>
    /// <returns></returns>
    public ColorFloat ColorPressing { get; set; } = new ColorFloat(1, 1, 1, 1);
    /// <summary>
    /// The color of the button in disabled state
    /// </summary>
    /// <returns></returns>
    public ColorFloat ColorDisabled { get; set; } = new ColorFloat(1, 1, 1, 1);

    //for TransitionMode.Transform, use TransitionTarget not used, transform will be applied to self
    private Transform2D _transformTweenStart = Transform2D.Identity;
    private Transform2D _transformTweenEnd = Transform2D.Identity;


    /// <summary>
    /// The transform of the button in normal state
    /// </summary>
    /// <value></value>
    public Transform2D TransformNormal { get; set; } = Transform2D.Identity;
    /// <summary>
    /// The transform of the button in hover state
    /// </summary>
    /// <value></value>
    public Transform2D TransformHover { get; set; } = Transform2D.Identity;
    /// <summary>
    /// The transform of the button in pressing state
    /// </summary>
    /// <value></value>
    public Transform2D TransformPressing { get; set; } = Transform2D.Identity;
    /// <summary>
    /// The transform of the button in disabled state
    /// </summary>
    /// <value></value>
    public Transform2D TransformDisabled { get; set; } = Transform2D.Identity;

    //for TransitionMode.SpriteSwap, use TransitionTarget as the target

    /// <summary>
    /// The sprite of the button in normal state
    /// </summary>
    /// <value></value>
    public Texture2D? SpriteNormal { get; set; } = null;
    /// <summary>
    /// The sprite of the button in hover state
    /// </summary>
    /// <value></value>
    public Texture2D? SpriteHover { get; set; } = null;
    /// <summary>
    /// The sprite of the button in pressing state
    /// </summary>
    /// <value></value>
    public Texture2D? SpritePressing { get; set; } = null;
    /// <summary>
    /// The sprite of the button in disabled state
    /// </summary>
    /// <value></value>
    public Texture2D? SpriteDisabled { get; set; } = null;

    //for TransitionMode.NodeSwap, TransitionTarget not used

    /// <summary>
    /// The node shown when the button in normal state
    /// </summary>
    /// <value></value>
    public UINode? NodeNormal { get; set; } = null;
    /// <summary>
    /// The node shown when the button in hover state
    /// </summary>
    /// <value></value>
    public UINode? NodeHover { get; set; } = null;
    /// <summary>
    /// The node shown when the button in pressing state
    /// </summary>
    /// <value></value>
    public UINode? NodePressing { get; set; } = null;
    /// <summary>
    /// The node shown when the button in disabled state
    /// </summary>
    /// <value></value>
    public UINode? NodeDisabled { get; set; } = null;

    /// <summary>
    /// The transition mode of the button
    /// </summary>
    /// <value></value>
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

        if (_selectableState == SelectableState.Hover && canvas.Hovered != this)
        {
            TryChangeTransitionState(SelectableState.Normal);
        }

        if (!_inTransition)
        {
            return;
        }

        if (FadeDuration == 0)
        {
            _tTransition = 1;
        }
        else
        {
            _tTransition += delta / FadeDuration;
        }

        float t = math.clamp(_tTransition, 0, 1);
        t = UtilsEasing.QuadInOut(t);
        if ((_transitionMode & TransitionMode.ColorTint) != 0)
        {
            UpdateColorTween(t);
        }

        if ((_transitionMode & TransitionMode.Transform) != 0)
        {
            UpdateTransform(t);
        }

        if (_tTransition >= 1)
        {
            _inTransition = false;
        }
    }


    public override void OnHover(Canvas canvas, Vector2 mousePosition)
    {
        base.OnHover(canvas, mousePosition);
        TryChangeTransitionState(SelectableState.Hover);
    }

    public override void OnPressDown(Canvas canvas, Vector2 mousePosition)
    {
        base.OnPressDown(canvas, mousePosition);
        TryChangeTransitionState(SelectableState.Pressing);
    }

    public override void OnPressUp(Canvas canvas, Vector2 mousePosition)
    {
        base.OnPressUp(canvas, mousePosition);
        TryChangeTransitionState(SelectableState.Normal);
    }

    private void TryChangeTransitionState(SelectableState state)
    {
        if (_selectableState != state)
        {
            _selectableState = state;
            OnTransitionStateChanged();
        }
    }

    private void OnTransitionStateChanged()
    {
        //use flag
        float tmpT = math.clamp(_tTransition, 0, 1);
        _tTransition = 0;
        _inTransition = true;
        if ((_transitionMode & TransitionMode.ColorTint) != 0)
        {
            ColorFloat current = ColorFloat.Lerp(_colorTweenStart, _colorTweenEnd, tmpT);
            StartColorTween(current, GetColorFromState(_selectableState));
        }

        if ((_transitionMode & TransitionMode.Transform) != 0)
        {
            Transform2D current = math.lerp(_transformTweenStart, _transformTweenEnd, tmpT);
            StartTransformTween(current, GetTransformFromState(_selectableState));
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
    }

    private void UpdateColorTween(float t)
    {
        if (_transitionSpriteTarget == null)
        {
            return;
        }

        _transitionSpriteTarget.Color = ColorFloat.Lerp(_colorTweenStart, _colorTweenEnd, t);
    }

    private void StartTransformTween(Transform2D start, Transform2D end)
    {
        _transformTweenStart = start;
        _transformTweenEnd = end;
    }

    private void UpdateTransform(float t)
    {
        if (_transitionTarget == null)
        {
            return;
        }

        _transitionTarget.LocalTransform = math.lerp(_transformTweenStart, _transformTweenEnd, t);
    }

    private void RefreshSpriteSwap()
    {
        if (_transitionSpriteTarget == null)
        {
            return;
        }

        switch (_selectableState)
        {
            case SelectableState.Normal:
                _transitionSpriteTarget.Texture = SpriteNormal;
                break;
            case SelectableState.Hover:
                _transitionSpriteTarget.Texture = SpriteHover;
                break;
            case SelectableState.Pressing:
                _transitionSpriteTarget.Texture = SpritePressing;
                break;
            case SelectableState.Disabled:
                _transitionSpriteTarget.Texture = SpriteDisabled;
                break;
            default:
                break;
        }
    }

    private void RefreshNodeSwap()
    {
        if (NodeNormal != null)
        {
            NodeNormal.IsEnable = _selectableState == SelectableState.Normal;
        }

        if (NodeHover != null)
        {
            NodeHover.IsEnable = _selectableState == SelectableState.Hover;
        }

        if (NodePressing != null)
        {
            NodePressing.IsEnable = _selectableState == SelectableState.Pressing;
        }

        if (NodeDisabled != null)
        {
            NodeDisabled.IsEnable = _selectableState == SelectableState.Disabled;
        }
    }

    private ColorFloat GetColorFromState(SelectableState state)
    {
        switch (state)
        {
            case SelectableState.Normal:
                return ColorNormal;
            case SelectableState.Hover:
                return ColorHover;
            case SelectableState.Pressing:
                return ColorPressing;
            case SelectableState.Disabled:
                return ColorDisabled;
            default:
                return ColorNormal;
        }
    }

    private Transform2D GetTransformFromState(SelectableState state)
    {
        switch (state)
        {
            case SelectableState.Normal:
                return TransformNormal;
            case SelectableState.Hover:
                return TransformHover;
            case SelectableState.Pressing:
                return TransformPressing;
            case SelectableState.Disabled:
                return TransformDisabled;
            default:
                return TransformNormal;
        }
    }


}