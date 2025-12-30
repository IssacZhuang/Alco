
namespace Alco.GUI;

/// <summary>
/// Defines how an image should be rendered.
/// </summary>
public enum ImageType
{
    /// <summary>
    /// Renders the image as-is with simple scaling.
    /// </summary>
    Simple,

    /// <summary>
    /// Renders the image with 9-slice scaling to preserve corners and edges.
    /// </summary>
    Sliced,

    /// <summary>
    /// Renders the image by repeating/tiling it across the target area.
    /// </summary>
    Tiled,
}
