namespace Alco;

/// <summary>
/// Defines the mode in which serialization is performed.
/// </summary>
public enum SerializeMode
{
    /// <summary>
    /// Serializes data to an output target (saving).
    /// </summary>
    Save = 0,
    /// <summary>
    /// Deserializes data from an input source (loading).
    /// </summary>
    Load = 1,
    /// <summary>
    /// Executes after loading to resolve references and finalize state.
    /// </summary>
    PostLoad = 2,
}