using System.Collections;
using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;

namespace Alco.Editor.Models;

public abstract class FileEditor
{
    private Control? _editControl;
    private Control? _previewControl;
    private FileInfo? _currentFile;

    public FileInfo? CurrentFile => _currentFile;

    public Control EditControl => _editControl ??= CreateEditControl();

    public Control PreviewControl => _previewControl ??= CreatePreviewControl();


    protected abstract Control CreateEditControl();

    protected abstract Control CreatePreviewControl();

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
