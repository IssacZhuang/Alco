namespace Alco.Editor.Models;

public enum FileSystemItemType
{
    None,
    Folder,
    File,
}

public class FileSystemItem
{
    public FileSystemItemType Type { get; set; }
    public string Path { get; set; } = string.Empty;
}

