using System;
using Avalonia.Controls;
using Alco.Editor.Views;

namespace Alco.Editor.ViewModels;

public class ExceptionInspector : Inspector<Exception>
{
    public override bool IsModified => false;

    private Exception _exception;

    public string ExceptionMessage { get; }
    public string StackTrace { get; }
    public string Title { get; }

    public ExceptionInspector(string title, Exception exception)
    {
        _exception = exception;
        ExceptionMessage = exception.Message;
        StackTrace = exception.StackTrace ?? "No stack trace";
        Title = title;
    }

    public override Control CreateControl()
    {
        return new Views.ExceptionInspector()
        {
            DataContext = this
        };
    }

    protected override void OnOpenAsset(EditorEngine engine, Exception asset)
    {
        throw new NotImplementedException();
    }
}