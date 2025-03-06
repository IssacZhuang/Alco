using Avalonia.Controls;
using Avalonia.Media;

namespace Alco.Editor.ViewModels;

public abstract class Page : ViewModelBase
{
    private bool _isActive;
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (SetProperty(ref _isActive, value))
            {
                if (value)
                    OnActivated();
                else
                    OnDeactivated();
            }
        }
    }

    public abstract Control Control { get; }

    public abstract string Name { get; }
    public abstract string IconKey { get; }
    public abstract string Tooltip { get; }

    public StreamGeometry IconGeometry => StreamGeometry.Parse(IconKey);

    protected virtual void OnActivated() { }
    protected virtual void OnDeactivated() { }
}

