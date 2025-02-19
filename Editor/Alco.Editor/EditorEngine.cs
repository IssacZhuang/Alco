using System;
using System.Collections.Generic;
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

    private DirectoryFileSource? _projectSource;

    public string CacheDirectory => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CacheFolderName);

    /// <summary>
    /// an independent asset system for the editor cache.
    /// </summary>
    public AssetSystem Cache { get; }

    public bool IsProjectOpen => _projectSource != null;
    public string ProjectDirectory { get; private set; }

    public IEnumerable<string> AllFilesInProject
    {
        get => Assets.AllFileNames;
    }

    public EditorEngine(GameEngineSetting setting) : base(setting)
    {
        //create cache folder if not exists
        if (!Directory.Exists(CacheDirectory))
        {
            Directory.CreateDirectory(CacheDirectory);
        }

        Cache = new AssetSystem(this, 0, true);
        DirectoryFileSource cacheSource = new(CacheDirectory);
        Cache.AddFileSource(cacheSource);

        AssetLoaderConfig configLoader = new AssetLoaderConfig(Cache);
        Cache.RegisterAssetLoader(configLoader);
        AssetEncoderConfig configEncoder = new AssetEncoderConfig();
        Cache.RegisterAssetEncoder(configEncoder);

        ProjectDirectory = string.Empty;
    }

    public void OpenProject(string projectPath)
    {
        if (IsProjectOpen)
        {
            throw new InvalidOperationException("Project is already open, close the current project first");
        }

        ProjectDirectory = projectPath;
        _projectSource = new DirectoryFileSource(projectPath);
        Assets.AddFileSource(_projectSource);
    }

    public void CloseProject()
    {
        if (_projectSource == null)
        {
            throw new InvalidOperationException("No project is open");
        }

        Assets.RemoveFileSource(_projectSource);
        _projectSource.Dispose();
        _projectSource = null;
    }

}
