namespace Vocore.Engine;

public struct AssetsSetting
{
    public int LoaderThreadCount { get; set; }
    public string AssetsPath { get; set; }
    public bool IsProfilingEnabled { get; set; }

    public static readonly AssetsSetting Default = new AssetsSetting
    {
        LoaderThreadCount = 2,
        AssetsPath = "Assets",
        IsProfilingEnabled = true
    };
}