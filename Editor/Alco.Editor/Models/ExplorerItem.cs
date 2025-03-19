namespace Alco.Editor.Models;

public enum ExplorerItemType
{
    None,
    /// <summary>
    /// A folder in the file system
    /// </summary>
    Folder,
    /// <summary>
    /// A file in the file system
    /// </summary>
    File,
    /// <summary>
    /// The type in C#
    /// </summary>
    Type
}

public class ExplorerItem
{
    public ExplorerItemType Type { get; set; }
    public string Path { get; set; } = string.Empty;
    public object? UserData { get; set; }
}

