using System.Numerics;
using Alco.Engine;
using Alco.Rendering;
using Alco;
using Alco.GUI;
using Alco.Graphics;
using Alco.IO;

using SandboxUtils;

using FastNoiseLite;

public class Game : GameEngine
{
    

    public Game(GameEngineSetting setting) : base(setting)
    {
        
    }

    protected override void InitializeDefaultAssetLoader(GameEngineSetting setting)
    {
        base.InitializeDefaultAssetLoader(setting);
        DirectoryWatcherFileSource fileSource1 = new DirectoryWatcherFileSource(Utils.GetBuiltInAssetsPath(), Assets);
        Assets.AddFileSource(fileSource1);
        DirectoryWatcherFileSource fileSource2 = new DirectoryWatcherFileSource(Utils.GetProjectAssetsPath(), Assets);
        Assets.AddFileSource(fileSource2);
    }

    protected override void OnUpdate(float delta)
    {
       
    }

    private void OnHotReload(string filename, object cachedAsset)
    {
       
    }

   
}