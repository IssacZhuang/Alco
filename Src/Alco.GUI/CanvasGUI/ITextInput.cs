namespace Alco.GUI;

/// <summary>
/// Defines the text-editing callbacks a <see cref="Canvas"/> sends to a focused text input control.
/// </summary>
public interface ITextInput
{
    /// <summary>Applies text entered while this control has text-input focus.</summary>
    /// <param name="canvas">The canvas dispatching the input.</param>
    /// <param name="text">The entered text.</param>
    public void OnTextInput(Canvas canvas, ReadOnlySpan<char> text);
    /// <summary>Handles the Delete key.</summary>
    public void HandleKeyDelete();
    /// <summary>Handles the Backspace key.</summary>
    public void HandleKeyBackspace();
    /// <summary>Handles the Enter key.</summary>
    public void HandleKeyEnter();
    /// <summary>Handles the Tab key.</summary>
    public void HandleKeyTab();
    /// <summary>Handles the Escape key.</summary>
    public void HandleKeyEscape();
    /// <summary>Handles the left arrow key.</summary>
    public void HandleKeyArrowLeft();
    /// <summary>Handles the right arrow key.</summary>
    public void HandleKeyArrowRight();
    /// <summary>Handles the up arrow key.</summary>
    public void HandleKeyArrowUp();
    /// <summary>Handles the down arrow key.</summary>
    public void HandleKeyArrowDown();
    /// <summary>Selects all text in the control.</summary>
    public void SelectAll();
    /// <summary>Gets the currently selected text.</summary>
    /// <returns>The selected text.</returns>
    public Span<char> GetSelectedText();
}