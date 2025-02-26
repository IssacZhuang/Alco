using System;
using Avalonia.Controls;

namespace Alco.Editor.ViewModels;

public abstract class PropertyEditor : ViewModelBase
{
    public AccessMemberInfo MemberInfo { get; }
    public object Target { get; }

    public PropertyEditor(object target, AccessMemberInfo memberInfo)
    {
        Target = target;
        MemberInfo = memberInfo;
    }

    public abstract Control CreateControl();

}
