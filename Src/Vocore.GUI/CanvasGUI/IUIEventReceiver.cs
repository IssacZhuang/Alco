namespace Vocore.GUI;

public interface IUIEventReceiver
{
    public void OnClick();
    public void OnHover();
    public void OnPressDown();
    public void OnPressUp();
}
