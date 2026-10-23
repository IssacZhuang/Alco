using System.Numerics;

namespace Alco.GUI
{
    /// <summary>
    /// Provides input state and text-input services consumed by a <see cref="Canvas"/>.
    /// </summary>
    public interface IUIInputTracker
    {
        /// <summary>
        /// The cursor position in the window (mouse or gamepad cursor).
        /// </summary>
        /// <value></value>
        public Vector2 CursorPosition { get; }
        /// <summary>
        /// The size of window.
        /// </summary>
        /// <value></value>
        public Vector2 WindowSize { get; }
        /// <summary>
        /// Indicates whether the mouse left button is currently being pressed.
        /// </summary>
        public bool IsMouseLeftPressing { get; }

        /// <summary>
        /// Indicates whether the confirm button (gamepad) is currently being pressed.
        /// </summary>
        public bool IsConfirmPressing { get; }

        /// <summary>Gets whether the Delete key is currently pressed.</summary>
        public bool IsKeyDeletePressing { get; }
        /// <summary>Gets whether the Backspace key is currently pressed.</summary>
        public bool IsKeyBackspacePressing { get; }
        /// <summary>Gets whether the Enter key is currently pressed.</summary>
        public bool IsKeyEnterPressing { get; }
        /// <summary>Gets whether the Tab key is currently pressed.</summary>
        public bool IsKeyTabPressing { get; }
        /// <summary>Gets whether the left arrow key is currently pressed.</summary>
        public bool IsKeyLeftPressing { get; }
        /// <summary>Gets whether the right arrow key is currently pressed.</summary>
        public bool IsKeyRightPressing { get; }
        /// <summary>Gets whether the up arrow key is currently pressed.</summary>
        public bool IsKeyUpPressing { get; }
        /// <summary>Gets whether the down arrow key is currently pressed.</summary>
        public bool IsKeyDownPressing { get; }
        /// <summary>Gets whether the select-all shortcut (Ctrl+A) is currently pressed.</summary>
        public bool IsKeySelectAllPressing { get; }

        /// <summary>Gets whether the copy shortcut (Ctrl+C) is currently pressed.</summary>
        public bool IsKeyCopyPressing { get; }

        /// <summary>Gets whether the paste shortcut (Ctrl+V) is currently pressed.</summary>
        public bool IsKeyPastePressing { get; }

        /// <summary>Gets the current scroll delta from the mouse wheel or gamepad stick.</summary>
        /// <param name="delta">The scroll delta when scrolling is active.</param>
        /// <returns><see langword="true"/> when scrolling is active; otherwise, <see langword="false"/>.</returns>
        public bool IsScrolling(out Vector2 delta);
        /// <summary>Sets the normalized area used for text input and the cursor position.</summary>
        /// <param name="xNorm">The normalized left edge of the input area.</param>
        /// <param name="yNorm">The normalized top edge of the input area.</param>
        /// <param name="widthNorm">The normalized width of the input area.</param>
        /// <param name="heightNorm">The normalized height of the input area.</param>
        /// <param name="cursor">The cursor position within the text.</param>
        public void SetTextInput(float xNorm, float yNorm, float widthNorm, float heightNorm, int cursor);
        /// <summary>Copies text to the clipboard.</summary>
        /// <param name="text">The text to copy.</param>
        public void CopyToClipboard(ReadOnlySpan<char> text);
        /// <summary>Gets the current clipboard text.</summary>
        /// <returns>The clipboard text.</returns>
        public ReadOnlySpan<char> GetClipboardText();

        /// <summary>Registers a handler for text input events.</summary>
        /// <param name="action">The handler to register.</param>
        public void RegisterTextInput(Action<ReadOnlySpan<char>> action);
        /// <summary>Unregisters a text input handler.</summary>
        /// <param name="action">The handler to unregister.</param>
        public void UnregisterTextInput(Action<ReadOnlySpan<char>> action);

        /// <summary>Requests text input from the window.</summary>
        public void RequestTextInput();
        /// <summary>Releases text input from the window.</summary>
        public void ReleaseTextInput();

        /// <summary>
        /// Indicates whether gamepad input is currently active.
        /// </summary>
        public bool IsGamepadInputting { get; }
    }
}