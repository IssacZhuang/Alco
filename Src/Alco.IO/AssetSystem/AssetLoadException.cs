namespace Alco.IO;

/// <summary>
/// Exception thrown when an asset fails to load.
/// </summary>
public class AssetLoadException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AssetLoadException"/> class with a descriptive message.
    /// </summary>
    /// <param name="message">The message that describes the asset load failure.</param>
    public AssetLoadException(string message) : base(message)
    {
    }
}
