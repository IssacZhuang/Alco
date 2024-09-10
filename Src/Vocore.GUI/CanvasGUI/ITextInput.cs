namespace Vocore.GUI;

public interface ITextInput
{
    public void OnTextInput(Canvas canvas,string text);
    public void HandleKeyDelete();
    public void HandleKeyBackspace();
    public void HandleKeyEnter();
    public void HandleKeyTab();
    public void HandleKeyEscape();
    public void HandleKeyArrowLeft();
    public void HandleKeyArrowRight();
    public void HandleKeyArrowUp();
    public void HandleKeyArrowDown();
}