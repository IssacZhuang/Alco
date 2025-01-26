using System.Numerics;

namespace Alco.GUI
{
    public interface IUIInputTracker
    {
        /// <summary>
        /// The mouse position in the window.
        /// </summary>
        /// <value></value>
        public Vector2 MousePosition { get; }
        /// <summary>
        /// The size of window.
        /// </summary>
        /// <value></value>
        public Vector2 WindowSize { get; }
        public bool IsMouseUp { get; }
        public bool IsMouseDown { get; }
        public bool IsMousePressing { get; }

        public bool IsKeyDeleteDown { get; }
        public bool IsKeyBackspaceDown { get; }
        public bool IsKeyEnterDown { get; }
        public bool IsKeyTabDown { get; }
        public bool IsKeyLeftDown { get; }
        public bool IsKeyRightDown { get; }
        public bool IsKeyUp { get; }
        public bool IsKeyDown { get; }
        public bool IsKeyLeft { get; }
        public bool IsKeyRight { get; }
        public bool IsKeySelectAllDown { get; }

        public bool IsKeyCopyDown { get; }

        public bool IsKeyPasteDown { get; }

        public bool IsMouseScrolling(out Vector2 delta);
        public void SetTextInput(float xNorm, float yNorm, float widthNorm, float heightNorm, int cursor);
        public void EndTextInput();
        public void CopyToClipboard(ReadOnlySpan<char> text);
        public ReadOnlySpan<char> GetClipboardText();

        public void RegisterTextInput(Action<string> action);
        public void UnregisterTextInput(Action<string> action);
    }
}