namespace Alco.Graphics;

/// <summary>
/// Thrown when slang compilation, linking or SPIR-V normalization fails.
/// </summary>
public class ShaderCompilationException : Exception
{
    public ShaderCompilationException(string message) : base(message)
    {
    }
}