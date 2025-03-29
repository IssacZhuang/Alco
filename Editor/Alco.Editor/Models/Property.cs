

using Avalonia.Controls;

namespace Alco.Editor.Models;

public class Property
{
    public string Name { get; }
    public string Description { get; }
    public Control Editor { get; }

    public Property(string name, string description, Control editor)
    {
        Name = name;
        Description = description;
        Editor = editor;
    }
}