using System;
using Avalonia.Controls;

namespace Alco.Editor.ViewModels;

public class ObjectPropertiesEditor : PropertyEditor
{
    private static readonly MemberAccessor s_memberAccessor = MemberAccessor.CreateCompatibleCachedAccessor();
    private static readonly ConcurrentLruCache<Type, AccessTypeInfo> _accessTypeInfos = new(64);
    public AccessTypeInfo AccessTypeInfo { get; }

    public ObjectPropertiesEditor(object target) : base(target, AccessMemberInfo.Empty)
    {
        AccessTypeInfo = GetAccessTypeInfo(target.GetType());
    }

    private static AccessTypeInfo GetAccessTypeInfo(Type type)
    {
        return _accessTypeInfos.GetOrAdd(type, static (t) => new AccessTypeInfo(t, s_memberAccessor));
    }

    public override Control CreateControl()
    {
        return new Views.ObjectPropertiesEditor()
        {
            DataContext = this,
        };
    }
}