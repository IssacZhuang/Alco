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
        public bool IsMousePressing { get; }

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

        public bool IsMouseScrolling(out Vector2 delta);
        public void SetTextInput(float xNorm, float yNorm, float widthNorm, float heightNorm, int cursor);
        public void CopyToClipboard(ReadOnlySpan<char> text);
        public ReadOnlySpan<char> GetClipboardText();

        public void RegisterTextInput(Action<ReadOnlySpan<char>> action);
        public void UnregisterTextInput(Action<ReadOnlySpan<char>> action);
    }
}