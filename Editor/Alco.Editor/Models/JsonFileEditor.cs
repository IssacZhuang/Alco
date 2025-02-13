using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;
using AvaloniaEdit;
using AvaloniaEdit.TextMate;
using AvaloniaEdit.Highlighting;
using TextMateSharp.Grammars;

namespace Alco.Editor.Models;

public class JsonFileEditor : FileEditor
{
    private TextEditor? _previewEditor;
    private TextMate.Installation? _textMateInstallation;


    protected override UserControl CreateEditControl()
    {
        // TODO: Implement JSON editor control
        return new UserControl();
    }

    protected override UserControl CreatePreviewControl()
    {
        var container = new UserControl();
        var editor = new TextEditor
        {
            ShowLineNumbers = true,
            IsReadOnly = true,
            FontFamily = "Cascadia Code,Consolas,Menlo,Monospace",
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };

        var registryOptions = new RegistryOptions(ThemeName.DarkPlus);
        _textMateInstallation = editor.InstallTextMate(registryOptions);
        _textMateInstallation.SetGrammar(registryOptions.GetScopeByLanguageId(registryOptions.GetLanguageByExtension(".json").Id));

        _previewEditor = editor;
        container.Content = editor;
        return container;
    }

    public override void OnOpenFile(FileInfo fileInfo)
    {
        base.OnOpenFile(fileInfo);
        if (_previewEditor != null && fileInfo.Exists)
        {
            _previewEditor.Text = File.ReadAllText(fileInfo.FullName);
        }
    }

    public override void SaveFile()
    {
        base.SaveFile();
        // TODO: Implement file saving logic
    }
}