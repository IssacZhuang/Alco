namespace Alco.Editor.ViewModels;

public class CreateFolderDialog : ViewModelBase
{
    public string Path { get; }

    public CreateFolderDialog(string path)
    {
        Path = path;
    }

    public Views.CreateFolderDialog CreateWindow()
    {
        return new Views.CreateFolderDialog
        {
            DataContext = this
        };
    }
}

