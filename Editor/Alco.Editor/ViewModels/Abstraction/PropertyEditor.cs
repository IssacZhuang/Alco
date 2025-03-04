using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.ComponentModel;
using Alco.Editor.Attributes;
using Alco.Editor.Views;
using Avalonia.Controls;

namespace Alco.Editor.ViewModels;

public abstract class PropertyEditor : ViewModelBase
{
    public AccessMemberInfo MemberInfo { get; }
    public object Target { get; }
    public virtual bool HasTitle => true;
    public PropertyEditor? Parent { get; set; }

    public PropertyEditor(object target, AccessMemberInfo memberInfo)
    {
        Target = target;
        MemberInfo = memberInfo;
    }

    public abstract Control CreateControl();

    public event Action? OnRefresh;

    private static readonly FrozenDictionary<Type, Type> _propertyEditors;
    static PropertyEditor()
    {
        Dictionary<Type, Type> typeToPropertyEditor = new();
        (Type, PropertyEditorAttribute)[] propertyEditors = UtilsAttribute.GetTypesWithAttribute<PropertyEditorAttribute>();
        foreach (var (type, attribute) in propertyEditors)
        {
            foreach (var supportedType in attribute.SupportedTypes)
            {
                typeToPropertyEditor[supportedType] = type;
            }
        }

        _propertyEditors = typeToPropertyEditor.ToFrozenDictionary();
    }

    public static PropertyEditor CreatePropertyEditor(object target, AccessMemberInfo memberInfo, uint depth = 0)
    {
        if (_propertyEditors.TryGetValue(memberInfo.MemberType, out Type? propertyEditorType))
        {
            return (PropertyEditor)Activator.CreateInstance(propertyEditorType, target, memberInfo)!;
        }

        if (!memberInfo.MemberType.IsClass)
        {
            return new PropertyEditorException(target, memberInfo, $"No property editor found for type {memberInfo.MemberType}");
        }

        object? value = memberInfo.GetValue<object>(target);

        if (value is null)
        {
            return new PropertyEditorException(target, memberInfo, $"Value is null for type {memberInfo.MemberType}");
        }

        if (PropertyListEditor.TryCreate(value, memberInfo.Name, depth + 1, out PropertyListEditor? propertyListEditor))
        {
            return propertyListEditor;
        }

        ObjectPropertiesEditor objectPropertiesEditor = new(value, memberInfo.Name, depth + 1);
        return objectPropertiesEditor;
    }

    public void Refresh()
    {
        OnRefresh?.Invoke();
        Parent?.Refresh();
    }
}
