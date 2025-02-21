namespace Alco.Editor.ViewModels;

public class RenameDialog : ViewModelBase
{
    public string Path { get; }
    public string OriginalName { get; }

    public RenameDialog(string path)
    {
        Path = path;
        OriginalName = System.IO.Path.GetFileName(path);
    }

    public Views.RenameDialog CreateWindow()
    {
        return new Views.RenameDialog
        {
            DataContext = this
        };
    }
}