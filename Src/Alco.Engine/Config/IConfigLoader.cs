namespace Alco.Engine;

public interface IConfigLoader
{
    /// <summary>
    /// The name of the config loader
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The file extension of the config loader
    /// </summary>
    IReadOnlyList<string> FileExtensions { get; }

    /// <summary>
    /// Creates a configuration object from raw data
    /// </summary>
    /// <param name="name">The name of the configuration</param>
    /// <param name="data">Raw configuration data to parse</param>
    /// <returns>Newly created Configable instance</returns>
    Configable CreateConfig(string name, ReadOnlySpan<byte> data);
}
