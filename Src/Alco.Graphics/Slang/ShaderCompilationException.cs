// Exception reported when shader loading, compilation, reflection, or Alco.Graphics ABI validation fails.

using Alco.Graphics.LowLevel;

namespace Alco.Graphics;

/// <summary>Thrown when shader compilation, linking, or Alco.Graphics ABI validation fails.</summary>
public class ShaderCompilationException : Exception
{
    /// <summary>The target format involved in the failure, or null when target-independent.</summary>
    public ShaderTarget? Target { get; }

    /// <summary>The shader module name involved in the failure, when known.</summary>
    public string? ModuleName { get; }

    /// <summary>Raw compiler diagnostics when reported by the backend compiler.</summary>
    public string? Diagnostics { get; }

    /// <summary>Initializes the exception with the specified message.</summary>
    /// <param name="message">The failure description.</param>
    public ShaderCompilationException(string message) : base(message)
    {
    }

    /// <summary>Initializes the exception with target, module, and diagnostic context.</summary>
    /// <param name="message">The failure description.</param>
    /// <param name="target">The compilation target.</param>
    /// <param name="moduleName">The module name.</param>
    /// <param name="diagnostics">Optional compiler diagnostic output.</param>
    public ShaderCompilationException(string message, ShaderTarget? target, string? moduleName, string? diagnostics = null) : base(message)
    {
        Target = target;
        ModuleName = moduleName;
        Diagnostics = diagnostics;
    }
}
