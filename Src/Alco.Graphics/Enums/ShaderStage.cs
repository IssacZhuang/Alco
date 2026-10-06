namespace Alco.Graphics;

/// <summary>
/// The shader pipeline stages a module or binding can target; combinable flags.
/// </summary>
[Flags]
public enum ShaderStage
{
    /// <summary>No stage.</summary>
    None = 0,
    /// <summary>The vertex stage.</summary>
    Vertex = 1 << 0,
    // currently not supported
    /// <summary>The hull (tessellation control) stage.</summary>
    Hull = 1 << 1,
    // currently not supported
    /// <summary>The domain (tessellation evaluation) stage.</summary>
    Domain = 1 << 2,
    // currently not supported
    /// <summary>The geometry stage.</summary>
    Geometry = 1 << 3,
    /// <summary>The fragment (pixel) stage.</summary>
    Fragment = 1 << 4,
    /// <summary>The compute stage.</summary>
    Compute = 1 << 5,
    /// <summary>Vertex, Fragment and Compute combined.</summary>
    Standard = Vertex | Fragment | Compute
}

/// <summary>
/// Convenience queries over <see cref="ShaderStage"/> flag combinations.
/// </summary>
public static class ShaderStageExtensions
{
    /// <summary>
    /// Whether the stage set includes both Vertex and Fragment; true only when
    /// BOTH bits are set (a graphics shader needs its whole pipeline).
    /// </summary>
    public static bool IsGraphicsShader(this ShaderStage stage)
    {
        return (stage & ShaderStage.Vertex) != 0 && (stage & ShaderStage.Fragment) != 0;
    }

    /// <summary>
    /// Whether the stage set includes the Compute bit.
    /// </summary>
    public static bool IsComputeShader(this ShaderStage stage)
    {
        return (stage & ShaderStage.Compute) != 0;
    }
}