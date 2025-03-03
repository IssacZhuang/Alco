using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Alco.Editor.Attributes;
using Avalonia.Controls;

namespace Alco.Editor.ViewModels;

public abstract class PropertyDictionaryEditor : PropertyEditor
{
    public abstract IEnumerable<Control> ItemEditors { get; }

    public PropertyDictionaryEditor(object target, AccessMemberInfo memberInfo) : base(target, memberInfo)
    {
    }

    public override Control CreateControl()
    {
        return new Views.PropertyDictionaryEditor()
        {
            DataContext = this,
        };
    }

    public abstract void Add();
    public abstract void Remove(object key);

    public static bool TryCreate(object dictionary, [NotNullWhen(true)] out PropertyDictionaryEditor? editor)
    {
        // Check if dictionary is generic dictionary
        if (!UtilsType.IsGenericDictionary(dictionary.GetType(), out Type? keyType, out Type? valueType))
        {
            editor = null;
            return false;
        }

        Type editorType = typeof(PropertyDictionaryEditor<,>).MakeGenericType(keyType, valueType);
        editor = (PropertyDictionaryEditor)Activator.CreateInstance(editorType, dictionary)!;
        return true;
    }
}

public class PropertyDictionaryEditor<TKey, TValue> : PropertyDictionaryEditor where TKey : notnull
{
    private readonly IDictionary<TKey, TValue> _dictionary;
    private readonly List<Control> _itemEditors = new();
    private static readonly bool IsValueClass = typeof(TValue).IsClass;

    public override IEnumerable<Control> ItemEditors => _itemEditors;

    public PropertyDictionaryEditor(IDictionary<TKey, TValue> dictionary) : base(dictionary, AccessMemberInfo.Empty)
    {
        _dictionary = dictionary;
        RefreshEditors();
    }

    private void RefreshEditors()
    {
        _itemEditors.Clear();
        foreach (var kvp in _dictionary)
        {
            // Create a property editor for the value using AccessDictionaryItemInfo
            PropertyEditor valueEditor = CreatePropertyEditor(_dictionary,
                new AccessDictionaryItemInfo<TKey, TValue>(_dictionary, kvp.Key));

            // Create a container for the key-value pair
            var container = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 5
            };

            // Add a label for the key
            var keyLabel = new TextBlock
            {
                Text = kvp.Key.ToString(),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Width = 150
            };

            var valueControl = valueEditor.CreateControl();

            container.Children.Add(keyLabel);
            container.Children.Add(valueControl);

            // Add a remove button
            var removeButton = new Button
            {
                Content = "X",
                Width = 30,
                Tag = kvp.Key
            };
            removeButton.Click += (s, e) =>
            {
                if (s is Button btn && btn.Tag is TKey key)
                {
                    Remove(key);
                }
            };
            container.Children.Add(removeButton);

            _itemEditors.Add(container);
        }
    }

    public override void Add()
    {
        // Create default key and value
        TKey key = CreateDefaultKey();
        TValue value = CreateDefaultValue();

        // Ensure the key is unique
        int counter = 0;
        while (_dictionary.ContainsKey(key))
        {
            if (key is string strKey)
            {
                key = (TKey)(object)$"Key{counter++}";
            }
            else if (key is int)
            {
                key = (TKey)(object)(counter++);
            }
            else
            {
                // For other key types, we might not be able to ensure uniqueness
                // Just try a few times and then give up
                if (counter > 100)
                    return;
                counter++;
                key = CreateDefaultKey();
            }
        }

        _dictionary.Add(key, value);
        RefreshEditors();
    }

    public override void Remove(object key)
    {
        if (key is TKey typedKey)
        {
            _dictionary.Remove(typedKey);
            RefreshEditors();
        }
    }

    private TKey CreateDefaultKey()
    {
        if (typeof(TKey) == typeof(string))
        {
            return (TKey)(object)"NewKey";
        }
        else if (typeof(TKey) == typeof(int))
        {
            return (TKey)(object)0;
        }
        else
        {
            // Try to create a default instance for other types
            try
            {
                return Activator.CreateInstance<TKey>();
            }
            catch
            {
                // If we can't create a default instance, return default
                return default!;
            }
        }
    }

    private TValue CreateDefaultValue()
    {
        if (IsValueClass)
        {
            try
            {
                return Activator.CreateInstance<TValue>();
            }
            catch
            {
                // If we can't create a default instance, return default
                return default!;
            }
        }
        else
        {
            return default!;
        }
    }
}