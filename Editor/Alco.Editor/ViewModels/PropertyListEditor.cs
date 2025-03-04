using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Alco.Editor.Attributes;
using Avalonia.Controls;

namespace Alco.Editor.ViewModels;

public abstract class PropertyListEditor : PropertyEditor
{
    public abstract IEnumerable<Control> ItemEditors { get; }

    public PropertyListEditor(object target, AccessMemberInfo memberInfo) : base(target, memberInfo)
    {
    }

    public override Control CreateControl()
    {
        return new Views.PropertyListEditor()
        {
            DataContext = this,
        };
    }

    public abstract void Add();
    public abstract void RemoveAt(int index);
    public abstract void RemoveLast();



    public static bool TryCreate(object list, [NotNullWhen(true)] out PropertyListEditor? editor)
    {
        //check if list is generic list
        if (!UtilsType.IsGenericList(list.GetType(), out Type? genericType))
        {
            editor = null;
            return false;
        }

        Type editorType = typeof(PropertyListEditor<>).MakeGenericType(genericType);
        editor = (PropertyListEditor)Activator.CreateInstance(editorType, list)!;
        return true;
    }
}

public class PropertyListEditor<T> : PropertyListEditor
{
    private static readonly bool IsClass = typeof(T).IsClass;
    private readonly IList<T> _list;
    
    private readonly List<Control> _itemEditors = new();

    public override IEnumerable<Control> ItemEditors => _itemEditors;

    public PropertyListEditor(IList<T> list) : base(list, AccessMemberInfo.Empty)
    {
        _list = list;
        foreach (var item in _list)
        {
            PropertyEditor propertyEditor = CreatePropertyEditor(_list, new AccessListItemInfo<T>(_list, _list.IndexOf(item)));
            propertyEditor.Parent = this;
            _itemEditors.Add(propertyEditor.CreateControl());
        }
    }

    public override void Add()
    {
        if (IsClass)
        {
            _list.Add(Activator.CreateInstance<T>());
        }
        else
        {
            _list.Add(default!);
        }

        PropertyEditor propertyEditor = CreatePropertyEditor(_list, new AccessListItemInfo<T>(_list, _list.Count - 1));
        propertyEditor.Parent = this;
        _itemEditors.Add(propertyEditor.CreateControl());
        Refresh();
    }

    public override void RemoveAt(int index)
    {
        _list.RemoveAt(index);
        _itemEditors.RemoveAt(index);
        Refresh();
    }

    public override void RemoveLast()
    {
        RemoveAt(_list.Count - 1);
    }
}

