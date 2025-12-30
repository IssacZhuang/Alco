namespace Alco.GUI;

/// <summary>
/// Provides methods for playing UI sounds.
/// </summary>
public interface IUISoundPlayer
{
    /// <summary>
    /// Plays the sound when a UI element is hovered.
    /// </summary>
    void PlayOnHoverSound();

    /// <summary>
    /// Plays the sound when a UI element is clicked.
    /// </summary>
    void PlayOnClickSound();
}
