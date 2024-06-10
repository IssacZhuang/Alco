namespace Vocore.Engine;

public struct AssetsSetting
{
    public int LoaderThreadCount { get; set; }

    public static readonly AssetsSetting Default = new AssetsSetting
    {
        LoaderThreadCount = 2
    };
}