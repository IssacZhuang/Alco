

using Avalonia.Controls;

namespace Alco.Editor.Models;

public class Property
{
    public string Name { get; }
    public Control Editor { get; }

    public Property(string name, Control editor)
    {
        Name = name;
        Editor = editor;
    }
}