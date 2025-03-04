using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;

namespace Alco.Editor.ViewModels;

public class ObjectPropertiesEditor : PropertyEditor
{
    private static readonly MemberAccessor s_memberAccessor = MemberAccessor.CreateCompatibleCachedAccessor();
    private static readonly ConcurrentLruCache<Type, AccessTypeInfo> _accessTypeInfos = new(64);
    private readonly List<(AccessMemberInfo, PropertyEditor)> _propertyEditors = new();
    
    public AccessTypeInfo AccessTypeInfo { get; }
    public uint Depth { get; }
    public string Header { get; }

    public override bool HasTitle => false;

    public IEnumerable<(AccessMemberInfo, PropertyEditor)> PropertyEditors => _propertyEditors;

    public ObjectPropertiesEditor(object target, string header, uint depth = 0) : base(target, AccessMemberInfo.Empty)
    {
        AccessTypeInfo = GetAccessTypeInfo(target.GetType());
        Depth = depth;
        Header = header;

        foreach (var member in AccessTypeInfo.Members)
        {
            PropertyEditor propertyEditor = CreatePropertyEditor(target, member, depth + 1);
            _propertyEditors.Add((member, propertyEditor));
            propertyEditor.Parent = this;
        }
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