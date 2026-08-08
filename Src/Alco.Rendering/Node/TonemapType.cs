
namespace Alco.Rendering;

/// <summary>
/// Supported tone mapping operators.
/// </summary>
public enum TonemapType
{
    /// <summary>
    /// Linear tone mapping (no tone mapping), directly copies the HDR buffer.
    /// </summary>
    Linear,
    /// <summary>Reinhard tone mapping.</summary>
    Reinhard,
    /// <summary>Uncharted 2 filmic tone mapping.</summary>
    Uncharted2,
    /// <summary>Filmic tone mapping.</summary>
    Filmic,
    /// <summary>ACES tone mapping.</summary>
    ACES,
    /// <summary>Neutral tone mapping.</summary>
    Neutral,
    /// <summary>AgX tone mapping.</summary>
    AgX,
}
