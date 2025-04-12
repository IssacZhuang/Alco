namespace Alco.Engine;

public struct AssetsSetting
{
    public string AssetsPath { get; set; }
    public bool IsProfilingEnabled { get; set; }

    public static readonly AssetsSetting Default = new AssetsSetting
    {
        AssetsPath = "Assets",
        IsProfilingEnabled = true
    };
}