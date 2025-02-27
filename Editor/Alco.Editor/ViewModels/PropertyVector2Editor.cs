using System;
using System.Drawing;
using System.Numerics;
using Alco.Editor.Attributes;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Alco.Editor.ViewModels;

[PropertyEditor(
    typeof(Vector2),
    typeof(int2),
    typeof(uint2),
    typeof(Half2))]
public class PropertyVector2Editor : PropertyEditor
{
    public PropertyVector2Editor(object target, AccessMemberInfo memberInfo) : base(target, memberInfo)
    {
    }


    public override Control CreateControl()
    {
        Control control = new Views.PropertyVector2Editor()
        {
            DataContext = this,
        };
        return control;
    }

}
