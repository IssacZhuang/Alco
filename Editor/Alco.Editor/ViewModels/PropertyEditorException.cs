using System;
using Alco.Editor.Attributes;
using Avalonia.Controls;

namespace Alco.Editor.ViewModels;

public class PropertyEditorException : PropertyEditor
{
    public string Message { get; }

    public PropertyEditorException(object target, AccessMemberInfo memberInfo, string message) : base(target, memberInfo)
    {
        Message = message;
    }

    public override Control CreateControl()
    {
        return new Views.PropertyEditorException()
        {
            DataContext = this,
        };
    }
}
