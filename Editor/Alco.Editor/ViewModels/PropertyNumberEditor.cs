using System;
using Alco.Editor.Attributes;
using Avalonia.Controls;
using Avalonia.Layout;

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
public class PropertyNumberEditor : PropertyEditor
{
    public PropertyNumberEditor(object target, AccessMemberInfo memberInfo) : base(target, memberInfo)
    {
    }


    public override Control CreateControl()
    {
        Control control = new Views.PropertyNumberEditor()
        {
            DataContext = this,
        };
        return control;
    }

}
