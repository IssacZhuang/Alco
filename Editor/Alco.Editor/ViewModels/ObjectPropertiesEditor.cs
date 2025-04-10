using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Alco.Editor.Models;
using Avalonia;
using Avalonia.Controls;

namespace Alco.Editor.ViewModels;

public class ObjectPropertiesEditor : PropertyEditor
{
    private static readonly MemberAccessor s_memberAccessor = MemberAccessor.CreateCompatibleCachedAccessor();
    private static readonly ConcurrentLruCache<Type, AccessTypeInfo> _accessTypeInfos = new(64);
    private static readonly object EmptyObject = new();
    
    private readonly List<(AccessMemberInfo, PropertyEditor)> _propertyEditors = new();
    
    public AccessTypeInfo AccessTypeInfo { get; }
    public uint Depth { get; }
    public string Header { get; }
    public bool IsNull => Target == EmptyObject;

    public override bool HasTitle => false;

    public IEnumerable<(AccessMemberInfo, PropertyEditor)> PropertyEditors => _propertyEditors;

    public ObservableCollection<Property> Properties { get; } = new();

    /// <summary>
    /// Create a new ObjectPropertiesEditor.
    /// </summary>
    /// <param name="target">The target object.</param>
    /// <param name="header">The header of the editor.</param>
    /// <param name="depth">The depth of the editor.</param>
    /// <param name="instanceType">Used to create a new instance of the target object if it is null.</param>
    public ObjectPropertiesEditor(object? target, string header, uint depth = 0, Type? instanceType = null) : base(target ?? EmptyObject, AccessMemberInfo.Empty)
    {
        target ??= EmptyObject;
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

    // must be called in UI thread
    public void SetupProperties()
    {
        Properties.Clear();
        foreach (var (member, propertyEditor) in _propertyEditors)
        {
            var property = new Property(member.Name, GetTypeName(member.MemberType), propertyEditor.CreateControl());
            Properties.Add(property);
        }
        OnPropertyChanged(nameof(Properties));
    }

    private static AccessTypeInfo GetAccessTypeInfo(Type type)
    {
        return _accessTypeInfos.GetOrAdd(type, static (t) => new AccessTypeInfo(t, s_memberAccessor));
    }

    private string GetTypeName(Type type)
    {
        if (type.IsGenericType)
        {
            string genericTypeName = type.GetGenericTypeDefinition().Name;
            string genericArguments = string.Join(", ", type.GetGenericArguments().Select(GetTypeName));
            return $"{type.Namespace}.{genericTypeName}<{genericArguments}>";
        }

        return $"{type.Namespace}.{type.Name}";
    }

    public override Control CreateControl()
    {
        Views.ObjectPropertiesEditor view = new()
        {
            DataContext = this,
        };
        
        return view;
    }
}