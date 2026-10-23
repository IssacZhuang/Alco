namespace Alco.Engine;

/// <summary>
/// Asset system configuration.
/// </summary>
public struct AssetsSetting
{
    /// <summary>
    /// Gets or sets the root directory that contains game assets.
    /// </summary>
    public string AssetsPath { get; set; }
    /// <summary>
    /// Gets or sets whether asset loading is profiled.
    /// </summary>
    public bool IsProfilingEnabled { get; set; }

    public static readonly AssetsSetting Default = new AssetsSetting
    {
        AssetsPath = "Assets",
        IsProfilingEnabled = true
    };
}