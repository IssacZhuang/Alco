namespace Alco.Graphics;

/// <summary>
/// Thrown when slang compilation, linking or SPIR-V normalization fails.
/// </summary>
public class ShaderCompilationException : Exception
{
    /// <summary>Initializes the exception with the specified message.</summary>
    /// <param name="message">The exception message.</param>
    public ShaderCompilationException(string message) : base(message)
    {
    }
}