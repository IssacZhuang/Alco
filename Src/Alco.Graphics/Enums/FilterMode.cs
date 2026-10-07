namespace Alco.Graphics;

/// <summary>
/// The filtering a sampler applies when sampling between texels.
/// </summary>
public enum FilterMode
{
    /// <summary>No filtering declared (invalid for usable samplers).</summary>
    None = 0,
    /// <summary>Nearest-neighbor sampling: the closest texel is used.</summary>
    Nearest = 1,
    /// <summary>Linear interpolation between neighboring texels.</summary>
    Linear = 2,
}