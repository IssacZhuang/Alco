using System.Numerics;

namespace Vocore.GUI
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
        public bool IsMouseScrolling(out Vector2 delta);
    }
}