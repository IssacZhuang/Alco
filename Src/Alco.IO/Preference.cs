using System.Text.Json;

namespace Alco.IO;

/// <summary>
/// A class for managing user preferences.
/// </summary>
public sealed class Preference
{
    private class Context
    {
        public Dictionary<string, bool> Bools { get; set; } = new Dictionary<string, bool>();
        public Dictionary<string, int> Ints { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, float> Floats { get; set; } = new Dictionary<string, float>();
        public Dictionary<string, string> Strings { get; set; } = new Dictionary<string, string>();
    }

    private readonly IFileSystem _fileSystem;
    private readonly string _filename;
    private readonly Context _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="Preference"/> class.
    /// </summary>
    /// <param name="fileSystem">The file system to use for storing preferences.</param>
    /// <param name="filename">The name of the preference file.</param>
    public Preference(IFileSystem fileSystem, string filename)
    {
        _fileSystem = fileSystem;
        _filename = filename;

        if (_fileSystem.TryGetData(_filename, out var handle, out _))
        {
            using (handle)
            {
                var context = JsonSerializer.Deserialize<Context>(handle.AsReadOnlySpan());
                _context = context ?? new Context();
            }
        }
        else
        {
            _context = new Context();
        }
    }

    /// <summary>
    /// Get a boolean value from preferences.
    /// </summary>
    /// <param name="key">The key of the preference.</param>
    /// <param name="defaultValue">The default value if the key is not found.</param>
    /// <returns>The value of the preference.</returns>
    public bool GetBool(string key, bool defaultValue = default) => _context.Bools.GetValueOrDefault(key, defaultValue);

    /// <summary>
    /// Set a boolean value in preferences.
    /// </summary>
    /// <param name="key">The key of the preference.</param>
    /// <param name="value">The value to set.</param>
    public void SetBool(string key, bool value) => _context.Bools[key] = value;

    /// <summary>
    /// Get an integer value from preferences.
    /// </summary>
    /// <param name="key">The key of the preference.</param>
    /// <param name="defaultValue">The default value if the key is not found.</param>
    /// <returns>The value of the preference.</returns>
    public int GetInt(string key, int defaultValue = default) => _context.Ints.GetValueOrDefault(key, defaultValue);

    /// <summary>
    /// Set an integer value in preferences.
    /// </summary>
    /// <param name="key">The key of the preference.</param>
    /// <param name="value">The value to set.</param>
    public void SetInt(string key, int value) => _context.Ints[key] = value;

    /// <summary>
    /// Get a float value from preferences.
    /// </summary>
    /// <param name="key">The key of the preference.</param>
    /// <param name="defaultValue">The default value if the key is not found.</param>
    /// <returns>The value of the preference.</returns>
    public float GetFloat(string key, float defaultValue = default) => _context.Floats.GetValueOrDefault(key, defaultValue);

    /// <summary>
    /// Set a float value in preferences.
    /// </summary>
    /// <param name="key">The key of the preference.</param>
    /// <param name="value">The value to set.</param>
    public void SetFloat(string key, float value) => _context.Floats[key] = value;

    /// <summary>
    /// Get a string value from preferences.
    /// </summary>
    /// <param name="key">The key of the preference.</param>
    /// <param name="defaultValue">The default value if the key is not found.</param>
    /// <returns>The value of the preference.</returns>
    public string GetString(string key, string defaultValue = "") => _context.Strings.GetValueOrDefault(key, defaultValue) ?? defaultValue;

    /// <summary>
    /// Set a string value in preferences.
    /// </summary>
    /// <param name="key">The key of the preference.</param>
    /// <param name="value">The value to set.</param>
    public void SetString(string key, string value) => _context.Strings[key] = value;

    /// <summary>
    /// Save the preferences to the file system.
    /// </summary>
    /// <returns>True if the preferences are successfully saved, false otherwise.</returns>
    public bool Save()
    {
        var data = JsonSerializer.SerializeToUtf8Bytes(_context);
        return _fileSystem.TryWriteFile(_filename, data, out _);
    }
}
