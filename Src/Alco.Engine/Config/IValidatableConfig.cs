
namespace Alco.Engine;

public interface IValidatableConfig
{
    /// <summary>
    /// Validate the properties in the config.
    /// </summary>
    /// <returns>The list of error messages.</returns>
    IEnumerable<string> Validate(GameEngine engine);
}
