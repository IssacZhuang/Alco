using Alco.Graphics;
using Alco;

namespace Alco.Engine;

/// <summary>
/// Directory-level texture import option. Nullable fields indicate "not specified" —
/// the value should be inherited from a parent directory or engine defaults.
/// Used by <c>.texture-option.meta</c> files.
/// </summary>
public class Texture2DImportOption : Meta
{
    /// <summary>
    /// Texture filtering mode. Null means inherit from parent directory or engine default.
    /// </summary>
    public FilterMode? FilterMode { get; set; }

    /// <summary>
    /// Texture address (wrap) mode. Null means inherit from parent directory or engine default.
    /// </summary>
    public AddressMode? AddressMode { get; set; }

    /// <summary>
    /// 9-slice padding. Null means inherit from parent directory or engine default.
    /// </summary>
    public Padding? SlicePadding { get; set; }
}
