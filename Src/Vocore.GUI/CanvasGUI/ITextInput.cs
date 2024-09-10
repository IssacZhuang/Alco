namespace Vocore.GUI;

public interface ITextInput
{
    public Span<char> TextSpan { get; }
    public void OnTextInput(Canvas canvas,string text);
    public void DeleteText(int start, int count);
}