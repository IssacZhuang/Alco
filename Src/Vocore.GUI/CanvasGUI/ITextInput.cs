namespace Vocore.GUI;

public interface ITextInput
{
    public BoundingBox2D InputArea { get; }
    public void OnTextInput(Canvas canvas,string text);
}