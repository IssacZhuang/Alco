namespace Alco.GUI;

/// <summary>
/// A minimal view over the canvas for navigation/hover coordination.
/// Exists so <see cref="NavigationHoverCoordinator"/> and other navigable
/// controls can be unit-tested without constructing the graphics-backed canvas.
/// </summary>
public interface INavigationContext
{
    /// <summary>
    /// The node currently under the cursor (mouse or gamepad cursor), or null.
    /// </summary>
    UINode? Hovered { get; }

    /// <summary>
    /// The input tracker providing directional/edge input state.
    /// </summary>
    IUIInputTracker InputTracker { get; }

    /// <summary>
    /// The navigable control that owns directional input this frame, or null.
    /// Determined automatically each frame as the last enabled
    /// <see cref="INavigationFocusable"/> in depth-first traversal order.
    /// </summary>
    INavigationFocusable? NavigationFocus { get; }

    /// <summary>
    /// Programmatically hovers a node (e.g. for D-Pad navigation).
    /// Mouse movement will naturally override this on the next frame the cursor moves.
    /// </summary>
    /// <param name="node">The node to hover, or null to clear hover.</param>
    void SetHovered(UINode? node);
}
