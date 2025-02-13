using System.Collections;
using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;

namespace Alco.Editor.Models;

public abstract class FileEditor
{
    private UserControl? _editControl;
    private UserControl? _previewControl;
    private FileInfo? _currentFile;

    public FileInfo? CurrentFile => _currentFile;

    public UserControl EditControl => _editControl ??= CreateEditControl();

    public UserControl PreviewControl => _previewControl ??= CreatePreviewControl();


    protected abstract UserControl CreateEditControl();

    protected abstract UserControl CreatePreviewControl();

    public virtual void OnOpenFile(FileInfo fileInfo)
    {
        _currentFile = fileInfo;
    }

    public virtual void OnCloseFile()
    {
        _currentFile = null;
    }

    public virtual void SaveFile()
    {
        if (_currentFile == null)
            return;
    }

    public virtual void SaveAs(FileInfo newFile)
    {
        _currentFile = newFile;
        SaveFile();
    }
}
