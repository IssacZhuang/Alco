using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;
using AvaloniaEdit;
using AvaloniaEdit.TextMate;
using AvaloniaEdit.Highlighting;
using TextMateSharp.Grammars;
using AvaloniaEdit.Document;

namespace Alco.Editor.Models;

public class JsonFileEditor : FileEditor
{
    private TextEditor? _previewEditor;
    private TextMate.Installation? _textMateInstallation;


    protected override Control CreateEditControl()
    {
        // TODO: Implement JSON editor control
        return new UserControl();
    }

    protected override Control CreatePreviewControl()
    {
        var editor = new TextEditor
        {
            ShowLineNumbers = true,
            IsReadOnly = true,
            FontFamily = "Cascadia Code,Consolas,Menlo,Monospace",
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
            MinHeight = 100,
            MinWidth = 100,
            Background = Avalonia.Media.Brushes.Transparent,
            Foreground = Avalonia.Media.Brushes.White,
            IsVisible = true,
            Options =
            {
                ShowBoxForControlCharacters = true,
                EnableHyperlinks = true,
                EnableEmailHyperlinks = true,
                AllowScrollBelowDocument = true,
                EnableTextDragDrop = true,
                EnableImeSupport = true,
                HighlightCurrentLine = true
            }
        };

        var registryOptions = new RegistryOptions(ThemeName.DarkPlus);
        _textMateInstallation = editor.InstallTextMate(registryOptions);
        _textMateInstallation.SetGrammar(registryOptions.GetScopeByLanguageId(registryOptions.GetLanguageByExtension(".json").Id));

        editor.TextArea.TextView.LinkTextForegroundBrush = Avalonia.Media.Brushes.Blue;
        editor.TextArea.TextView.LinkTextUnderline = true;

        _previewEditor = editor;

        return editor;
    }

    public override void OnOpenFile(FileInfo fileInfo)
    {
        base.OnOpenFile(fileInfo);
        if (_previewEditor != null && fileInfo.Exists)
        {
            var text = File.ReadAllText(fileInfo.FullName);
            var document = new TextDocument(text);
            _previewEditor.Document = document;

            // Force a refresh of the editor
            _previewEditor.TextArea.TextView.Redraw();
        }
    }

    public override void SaveFile()
    {
        base.SaveFile();
        // TODO: Implement file saving logic
    }
}