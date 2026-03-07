namespace Alco.GUI;

/// <summary>
/// Interface for UI nodes that support keyboard/D-pad navigation.
/// During each frame, only the last enabled <see cref="INavigationFocusable"/>
/// in the node-tree traversal order will process directional input.
/// </summary>
public interface INavigationFocusable
{
    /// <summary>
    /// Whether this node currently wants to process navigation input.
    /// </summary>
    bool CanNavigate { get; }
}
