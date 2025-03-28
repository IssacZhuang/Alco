using Avalonia.Controls;

namespace Alco.Editor.ViewModels;

public class InspectorTabItem : ViewModelBase
{
    private readonly Inspector _inspector;
    public bool IsModified => _inspector.IsModified;
    public string Filename { get; }
    public string Path { get; }
    public Control Content { get; }
    public bool IsPinned { get; set; } = false;


    public InspectorTabItem(Inspector inspector, string path)
    {
        _inspector = inspector;
        Path = path;
        Filename = System.IO.Path.GetFileName(path);
        Content = inspector.CreateControl();
    }

}
