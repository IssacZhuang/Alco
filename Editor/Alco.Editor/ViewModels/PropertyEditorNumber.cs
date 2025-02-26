using System;
using Alco.Editor.Attributes;
using Avalonia.Controls;

namespace Alco.Editor.ViewModels;

[PropertyEditor(
    typeof(int),
    typeof(uint),
    typeof(long),
    typeof(ulong),
    typeof(short),
    typeof(ushort),
    typeof(byte),
    typeof(sbyte),
    typeof(float),
    typeof(double),
    typeof(Half),
    typeof(decimal))]
public class PropertyEditorNumber : PropertyEditor
{
    public PropertyEditorNumber(object target, AccessMemberInfo memberInfo) : base(target, memberInfo)
    {
    }


    public override Control CreateControl()
    {
        return new Views.PropertyEditorNumber()
        {
            DataContext = this,
        };
    }

}
