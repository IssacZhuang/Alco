using System.Numerics;

namespace Vocore.GUI;

public interface IScrollable
{

    /// <summary>
    /// Whether the scrollable should ignore occlusion for dragging.
    /// </summary>
    /// <value></value>
    public bool IgnoreOcclusion { get; }

    /// <summary>
    /// Called by mouse wheel scroll. It will not affect by the occulusion.
    /// </summary>
    /// <param name="displacement"></param>
    void OnScroll(Vector2 displacement);
}