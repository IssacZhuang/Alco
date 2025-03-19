using System;
using Avalonia.Controls;
using Alco.Editor.Views;

namespace Alco.Editor.ViewModels;

public class InspectorForException : Inspector<Exception>
{
    public override bool IsModified => false;

    private Exception _exception;

    public string ExceptionMessage { get; }
    public string StackTrace { get; }
    public string Title { get; }

    public InspectorForException(string title, Exception exception )
    {
        _exception = exception;
        ExceptionMessage = exception.Message;
        StackTrace = exception.StackTrace ?? "No stack trace";
        Title = title;
    }

    public override Control CreateControl()
    {
        return new Views.InspectorForException()
        {
            DataContext = this
        };
    }

    protected override void OnOpenAsset(EditorEngine engine, Exception asset, string path)
    {
        throw new NotImplementedException();
    }

    public override void SaveAsset(EditorEngine engine)
    {
        throw new NotImplementedException();
    }
}