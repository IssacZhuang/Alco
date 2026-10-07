namespace Alco.Graphics;

/// <summary>
/// Thrown when a shader's reflected interface is invalid (duplicate resource
/// names, non-contiguous sets, more sets than the device allows).
/// </summary>
public class ShaderReflectionException : Exception
{
    public ShaderReflectionException(string message) : base(message)
    {
    }
}