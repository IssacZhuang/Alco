using System;
using Avalonia;
using Avalonia.Controls;

namespace Alco.Editor.ViewModels;

public class ObjectPropertiesEditor : PropertyEditor
{
    private static readonly MemberAccessor s_memberAccessor = MemberAccessor.CreateCompatibleCachedAccessor();
    private static readonly ConcurrentLruCache<Type, AccessTypeInfo> _accessTypeInfos = new(64);
    public AccessTypeInfo AccessTypeInfo { get; }
    public uint Depth { get; }

    public ObjectPropertiesEditor(object target, uint depth = 0) : base(target, AccessMemberInfo.Empty)
    {
        AccessTypeInfo = GetAccessTypeInfo(target.GetType());
        Depth = depth;
    }

    private static AccessTypeInfo GetAccessTypeInfo(Type type)
    {
        return _accessTypeInfos.GetOrAdd(type, static (t) => new AccessTypeInfo(t, s_memberAccessor));
    }

    public override Control CreateControl()
    {
        Views.ObjectPropertiesEditor view = new()
        {
            DataContext = this,
        };
        if (Depth > 0)
        {
            view.Margin = new Thickness(15, 0, 0, 0);
        }
        else
        {
            view.Margin = new Thickness(0, 0, 0, 0);
        }
        return view;
    }
}