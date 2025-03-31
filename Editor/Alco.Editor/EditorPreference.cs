using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.Json;
using System.Web;
using Alco.Editor.Models;
using Alco.IO;

namespace Alco.Editor;

public class EditorPreference
{
    private const string TmpFolderName = ".tmp";
    private const string PreferenceFileName = "preference.json";
    private PreferenceConfig Config { get; }
    private readonly string _tmpDirectory = Path.Combine(Environment.CurrentDirectory, TmpFolderName);
    private readonly string _preferenceFilePath;
    private readonly EditorEngine _engine;
    private readonly JsonSerializerOptions _jsonSerializerOptions;

    public EditorPreference(EditorEngine engine)
    {
        _engine = engine;
        _preferenceFilePath = Path.Combine(_tmpDirectory, PreferenceFileName);
        _jsonSerializerOptions = BaseConfig.BuildJsonSerializerOptions();
        
        if (!Directory.Exists(_tmpDirectory))
        {
            Directory.CreateDirectory(_tmpDirectory);
        }

        if (File.Exists(_preferenceFilePath))
        {
            try
            {
                string json = File.ReadAllText(_preferenceFilePath);
                Config = JsonSerializer.Deserialize<PreferenceConfig>(json, _jsonSerializerOptions)!;
            }
            catch (Exception e)
            {
                Log.Error($"Failed to load preference file: {_preferenceFilePath}", e);
                Config = new PreferenceConfig();
            }
        }
        else
        {
            Config = new PreferenceConfig();
        }

        TryOpenProject(Config.OpenedProject);
    }

    public bool TryGetString(string key, [MaybeNullWhen(false)] out string value)
    {
        return Config.Strings.TryGetValue(key, out value);
    }

    public void SetString(string key, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Config.Strings[key] = value;
    }

    public bool TryGetFloat(string key, out float value)
    {
        return Config.Floats.TryGetValue(key, out value);
    }

    public void SetFloat(string key, float value)
    {
        Config.Floats[key] = value;
    }

    public bool TryGetDouble(string key, out double value)
    {
        return Config.Doubles.TryGetValue(key, out value);
    }

    public void SetDouble(string key, double value)
    {
        Config.Doubles[key] = value;
    }

    public bool TryGetBool(string key, out bool value)
    {
        return Config.Bools.TryGetValue(key, out value);
    }

    public void SetBool(string key, bool value)
    {
        Config.Bools[key] = value;
    }

    public bool TryGetInt(string key, out int value)
    {
        return Config.Ints.TryGetValue(key, out value);
    }

    public void SetInt(string key, int value)
    {
        Config.Ints[key] = value;
    }

    public void Save()
    {
        string projectPath = _engine.Project?.FullPath ?? string.Empty;
        Config.OpenedProject = projectPath;
        
        string json = JsonSerializer.Serialize(Config, _jsonSerializerOptions);
        File.WriteAllText(_preferenceFilePath, json);
    }

    private void TryOpenProject(string projectFilePath)
    {
        if (string.IsNullOrEmpty(projectFilePath))
        {
            return;
        }

        try
        {
            _engine.OpenProject(projectFilePath);
        }
        catch (Exception e)
        {
            Log.Error($"Failed to open project: {projectFilePath}", e);
        }
    }
}

