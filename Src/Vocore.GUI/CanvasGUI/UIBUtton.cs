using System.Runtime.CompilerServices;
using Vocore.Graphics;
using Vocore.Rendering;

namespace Vocore.GUI;

public class UIButton : UINode, IUIEventReceiver
{
    private event Action? _onClickEvent;
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

    public event Action OnClickEvent
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        add => _onClickEvent += value;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        remove => _onClickEvent -= value;
    }

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

    
    protected override void OnTick(Canvas canvas, float delta)
    {
        Transform2D transform = RenderTransform;
        ShapeBox2D shape = new ShapeBox2D(transform.position, transform.scale);
        canvas.AddClickReciever(this, shape);
    }

    public void OnClick()
    {
        _onClickEvent?.Invoke();
    }

    public void OnHover()
    {
        throw new NotImplementedException();
    }

    public void OnPressDown()
    {
        throw new NotImplementedException();
    }

    public void OnPressUp()
    {
        throw new NotImplementedException();
    }
}