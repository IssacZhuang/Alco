namespace Alco.Editor.Models;

public enum ObjectType
{
    None,
    Blob,
    Tree,
    Tag,
    Commit,
}

public class Object
{
    public ObjectType Type { get; set; }
    public string Path { get; set; }
}

