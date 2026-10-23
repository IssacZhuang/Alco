namespace Alco;

/// <summary>
/// Implemented by objects that expose their parameters for interactive editing through an <see cref="IInspector"/>.
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
