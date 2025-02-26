using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using Alco.Editor.Attributes;
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

    public static PropertyEditor CreatePropertyEditor(object target, AccessMemberInfo memberInfo)
    {
        if (_propertyEditors.TryGetValue(memberInfo.MemberType, out Type? propertyEditorType))
        {
            return (PropertyEditor)Activator.CreateInstance(propertyEditorType, target, memberInfo)!;
        }
        //todo: fallback to a hint control
        throw new Exception($"No property editor found for type {memberInfo.MemberType}");
    }
}
