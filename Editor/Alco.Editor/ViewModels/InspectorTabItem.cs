
using Avalonia.Controls;

namespace Alco.Editor.ViewModels;

public class InspectorTabItem : ViewModelBase
{
    private readonly Inspector _inspector;
    public bool IsModified => _inspector.IsModified;
    public string Filename { get; }
    public Control Content { get; }


    public InspectorTabItem(Inspector inspector, string filename)
    {
        _inspector = inspector;
        Filename = filename;
        Content = inspector.CreateControl();
    }

}
