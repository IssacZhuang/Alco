using Avalonia.Controls;
using Avalonia.Media;

namespace Alco.Editor.ViewModels;

public abstract class Inspector : ViewModelBase
{
    public abstract Control Control { get; }
    public abstract bool IsModified { get; }
}
