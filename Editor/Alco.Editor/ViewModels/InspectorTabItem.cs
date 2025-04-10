using Avalonia.Controls;

namespace Alco.Editor.ViewModels;

public class InspectorTabItem : ViewModelBase
{
    private readonly Inspector _inspector;
    private bool _isModified;

    public bool IsModified
    {
        get => _isModified;
        private set
        {
            if (_isModified != value)
            {
                _isModified = value;
                OnPropertyChanged(nameof(IsModified));
            }
        }
    }

    public string Filename { get; }
    public string Path { get; }
    public Control Content { get; }
    public bool IsPinned { get; set; } = false;


    public InspectorTabItem(Inspector inspector, string path)
    {
        _inspector = inspector;
        _isModified = _inspector.IsModified;
        Path = path;
        Filename = System.IO.Path.GetFileName(path);
        Content = inspector.CreateControl();

        //subscribe to inspector changes
        _inspector.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(Inspector.IsModified))
            {
                IsModified = _inspector.IsModified;
            }
        };
    }
}
