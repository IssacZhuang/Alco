using System.Numerics;

namespace Vocore.GUI;

public interface IScrollable
{
    /// <summary>
    /// Called by mouse drag and mouse wheel.
    /// </summary>
    /// <param name="displacement"></param>
    void OnScroll(Vector2 displacement);
}