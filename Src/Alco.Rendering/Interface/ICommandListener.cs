using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Receives callbacks around a rendering command submission.
/// </summary>
public interface ICommandListener
{
    /// <summary>
    /// Called before the command is submitted.
    /// </summary>
    public void OnCommandBegin();

    /// <summary>
    /// Called after the command has been submitted.
    /// </summary>
    public void OnCommandEnd();
}