using System.Numerics;

namespace Vocore.GUI
{
    public interface IUIInputTracker
    {
        public Vector2 MousePosition { get; }
        public bool IsMouseClicked { get; }
        public bool IsMousePressing { get; }
    }
}