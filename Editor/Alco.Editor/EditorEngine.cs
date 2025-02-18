using System;
using System.IO;
using Alco.Engine;
using Alco.IO;

namespace Alco.Editor;

/// <summary>
/// The engine for the editor.
/// </summary>
public class EditorEngine : GameEngine
{
    public const string CacheFolderName = ".cache";

    public string CacheFolderPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CacheFolderName);

    /// <summary>
    /// an independent asset system for the editor cache.
    /// </summary>
    public AssetSystem Cache { get; }

    public EditorEngine(GameEngineSetting setting) : base(setting)
    {
        //create cache folder if not exists
        if (!Directory.Exists(CacheFolderPath))
        {
            Directory.CreateDirectory(CacheFolderPath);
        }

        Cache = new AssetSystem(this, 1, true);
        DirectoryFileSource cacheSource = new(CacheFolderPath);
        Cache.AddFileSource(cacheSource);

        AssetLoaderConfig configLoader = new AssetLoaderConfig(Cache);
        Cache.RegisterAssetLoader(configLoader);
        AssetEncoderConfig configEncoder = new AssetEncoderConfig();
        Cache.RegisterAssetEncoder(configEncoder);
    }

}
