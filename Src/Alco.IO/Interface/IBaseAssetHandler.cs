/// <summary>
/// The type less abstract asset handler interface
/// </summary>
public interface IBaseAssetHandler
{
    /// <summary>
    /// The name of the asset loader
    /// </summary>
    string Name { get; }
    /// <summary>
    /// The file extension of the asset loader
    /// </summary>
    IReadOnlyList<string> FileExtensions { get; }
}