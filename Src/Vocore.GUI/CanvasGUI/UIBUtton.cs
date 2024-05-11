using System.Runtime.CompilerServices;

namespace Vocore.GUI;

public class UIButton: UINode, IClickable
{
    private event Action? _onClickEvent;

    public event Action OnClickEvent
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        add => _onClickEvent += value;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        remove => _onClickEvent -= value;
    }

    public UIButton()
    {
        
    }

    public void OnClick()
    {
        _onClickEvent?.Invoke();
    }

    protected override void OnUpdate(Canvas canvas, float delta)
    {
        Transform2D transform = RenderTransform;
        ShapeBox2D shape = new ShapeBox2D(transform.position, transform.scale);
        canvas.AddClickReciever(this, shape);
    }
}