using Avalonia.Controls;

namespace Alco.Editor.ViewModels;

public class InspectorTabItem : ViewModelBase
{
    private Inspector? _inspector;
    private readonly Views.Spinner _loading;
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
    public Control Content { get; private set; }
    public bool IsPinned { get; set; } = false;
    public bool IsLoading { get; set; } = false;


    public InspectorTabItem(string path)
    {
        _isModified = false;
        Path = path;
        Filename = System.IO.Path.GetFileName(path);
        // Content = inspector.CreateControl();
        _loading = new Views.Spinner();
        Content = _loading;

        //subscribe to inspector changes
    }

    public void SetInspector(Inspector inspector)
    {
        _inspector = inspector;
        _isModified = _inspector.IsModified;
        _inspector.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(Inspector.IsModified))
            {
                IsModified = _inspector.IsModified;
            }
        };
        Content = _inspector.CreateControl();
        OnPropertyChanged(nameof(Content));
    }
}
