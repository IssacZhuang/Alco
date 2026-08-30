namespace Alco;

/// <summary>
/// Implemented by objects (typically engine assets and descriptor types) that
/// can expose their parameters for interactive editing through an
/// <see cref="IInspector"/>. Implementations stay UI-agnostic: they only call
/// widgets on the passed inspector. Hosts (the future editor, debug overlays)
/// discover editability by type check — <c>obj is IInspectable</c> — since
/// engine objects share no common asset base class.
/// </summary>
public interface IInspectable
{
    /// <summary>
    /// Draws the editing widgets for this object's parameters. Called every
    /// frame while the object is selected in an editing UI; write edits
    /// directly back to the object's state.
    /// </summary>
    void Inspect(IInspector inspector);
}
