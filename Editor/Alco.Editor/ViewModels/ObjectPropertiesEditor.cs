using System;
using Avalonia.Controls;

namespace Alco.Editor.ViewModels;

public class ObjectPropertiesEditor : ViewModelBase
{
    private static readonly MemberAccessor s_memberAccessor = MemberAccessor.CreateCompatibleCachedAccessor();
    private static readonly ConcurrentLruCache<Type, AccessTypeInfo> _accessTypeInfos = new(64);
    public object Target { get; }
    public AccessTypeInfo AccessTypeInfo { get; }

    public ObjectPropertiesEditor(object target)
    {
        Target = target;
        AccessTypeInfo = GetAccessTypeInfo(target.GetType());
    }

    private static AccessTypeInfo GetAccessTypeInfo(Type type)
    {
        return _accessTypeInfos.GetOrAdd(type, static (t) => new AccessTypeInfo(t, s_memberAccessor));
    }
}