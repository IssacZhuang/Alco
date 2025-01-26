namespace Alco.GUI;

public interface ITextInput
{
    public void OnTextInput(Canvas canvas, ReadOnlySpan<char> text);
    public void HandleKeyDelete();
    public void HandleKeyBackspace();
    public void HandleKeyEnter();
    public void HandleKeyTab();
    public void HandleKeyEscape();
    public void HandleKeyArrowLeft();
    public void HandleKeyArrowRight();
    public void HandleKeyArrowUp();
    public void HandleKeyArrowDown();
    public void SelectAll();
    public Span<char> GetSelectedText();
}