
namespace Alco.Engine;

public interface IValidatableConfig
{
    /// <summary>
    /// Validate the properties in the config.
    /// </summary>
    /// <returns>The list of error messages.</returns>
    IEnumerable<string> Validate(GameEngine engine);

    /// <summary>
    /// Validate the properties in the config with a try-catch block.
    /// </summary>
    /// <param name="engine">The game engine.</param>
    /// <returns>The list of error messages.</returns>
    IEnumerable<string> ValidateSafely(GameEngine engine)
    {
        try
        {
            return Validate(engine);
        }
        catch (Exception ex)
        {
            return [ex.Message];
        }
    }
}
