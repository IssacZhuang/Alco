using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using Alco.Editor.Attributes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Alco.Editor.ViewModels;

public abstract class PropertyListEditor : PropertyEditor
{
    public abstract IEnumerable<(PropertyEditor, Control)> ItemEditors { get; }
    public abstract Type ListType { get; }
    public abstract Type ItemType { get; }
    public abstract int ItemCount { get; }
    public uint Depth { get; }
    public string Header { get; set; } = string.Empty;

    public PropertyListEditor(object target, AccessMemberInfo memberInfo, uint depth) : base(target, memberInfo)
    {
        Depth = depth;
    }

    public override Control CreateControl()
    {
        Views.PropertyListEditor view = new()
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

    public override bool HasTitle => false;

    public abstract void Add();
    public abstract void RemoveAt(int index);
    public abstract void RemoveLast();



    public static bool TryCreate(object list, string header, uint depth, [NotNullWhen(true)] out PropertyListEditor? editor)
    {
        //check if list is generic list
        if (!UtilsType.IsGenericList(list.GetType(), out Type? genericType))
        {
            editor = null;
            return false;
        }

        Type editorType = typeof(PropertyListEditor<>).MakeGenericType(genericType);
        editor = (PropertyListEditor)Activator.CreateInstance(editorType, list, depth)!;
        editor.Header = header;
        return true;
    }
}

public class PropertyListEditor<T> : PropertyListEditor
{
    private static readonly bool IsClass = typeof(T).IsClass;
    private readonly IList<T> _list;
    private readonly List<(PropertyEditor, Control)> _itemEditors = new();
    public override IEnumerable<(PropertyEditor, Control)> ItemEditors => _itemEditors;
    public override Type ItemType => typeof(T);
    public override Type ListType => _list.GetType().GetGenericTypeDefinition();
    public override int ItemCount => _list.Count;

    public PropertyListEditor(IList<T> list, uint depth) : base(list, AccessMemberInfo.Empty, depth)
    {
        _list = list;
        for (int i = 0; i < _list.Count; i++)
        {
            AddControl(i);
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

        AddControl(_list.Count - 1);
        Refresh();
    }

    public override void RemoveAt(int index)
    {
        _list.RemoveAt(index);
        _itemEditors.RemoveAt(index);
        UpdateAccessIndex();
        Refresh();
    }

    public override void RemoveLast()
    {
        RemoveAt(_list.Count - 1);
    }

    private void UpdateAccessIndex()
    {
        for (int i = 0; i < _itemEditors.Count; i++)
        {
            (_itemEditors[i].Item1 as AccessListItemInfo<T>)!.Index = i;
        }
    }

    private void AddControl(int index)
    {
        Grid grid = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        // Define columns: fixed width for button, remaining space for property editor
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Button btnRemove = new Button()
        {
            Content = "-",
            Width = 32,
            Margin = new Thickness(10, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        //the index in the AccessListItemInfo might be changed during remove
        AccessListItemInfo<T> accessListItemInfo = new(_list, index);

        btnRemove.Click += (sender, e) => RemoveAt(accessListItemInfo.Index);
        Grid.SetColumn(btnRemove, 0);
        grid.Children.Add(btnRemove);

        PropertyEditor propertyEditor = CreatePropertyEditor(_list, accessListItemInfo);
        propertyEditor.Parent = this;
        Control propertyControl = propertyEditor.CreateControl();
        Grid.SetColumn(propertyControl, 1);
        grid.Children.Add(propertyControl);
        _itemEditors.Add((propertyEditor, grid));
    }
}

