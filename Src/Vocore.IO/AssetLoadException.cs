namespace Vocore.IO;

/// <summary>
/// Exception thrown when an asset fails to load.
/// </summary>
public class AssetLoadException : Exception
{
    public AssetLoadException(string message) : base(message)
    {
    }
}