using System.Numerics;

namespace Alco.GUI
{
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

        public bool IsKeyDeletePressing { get; }
        public bool IsKeyBackspacePressing { get; }
        public bool IsKeyEnterPressing { get; }
        public bool IsKeyTabPressing { get; }
        public bool IsKeyLeftPressing { get; }
        public bool IsKeyRightPressing { get; }
        public bool IsKeyUpPressing { get; }
        public bool IsKeyDownPressing { get; }
        public bool IsKeySelectAllPressing { get; }

        public bool IsKeyCopyPressing { get; }

        public bool IsKeyPastePressing { get; }

        public bool IsScrolling(out Vector2 delta);
        public void SetTextInput(float xNorm, float yNorm, float widthNorm, float heightNorm, int cursor);
        public void CopyToClipboard(ReadOnlySpan<char> text);
        public ReadOnlySpan<char> GetClipboardText();

        public void RegisterTextInput(Action<ReadOnlySpan<char>> action);
        public void UnregisterTextInput(Action<ReadOnlySpan<char>> action);

        public void RequestTextInput();
        public void ReleaseTextInput();
    }
}