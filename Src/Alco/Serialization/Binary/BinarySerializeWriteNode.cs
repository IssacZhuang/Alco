using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Alco;

public class BinarySerializeWriteNode : SerializeWriteNode
{
    private readonly ReferenceContext? _referenceContext;
    protected BinaryTable _content = new BinaryTable();
    public BinaryTable Content => _content;

    public override ReferenceContext? ReferenceContext => _referenceContext;

    public BinarySerializeWriteNode(ReferenceContext? referenceContext, Action<string>? onError = null)
    {
        _referenceContext = referenceContext;
        OnError = onError;
    }

    /// <summary>
    /// Binds a complex object that implements ISerializable for serialization.
    /// Handles exceptions gracefully to prevent serialization errors from affecting other nodes.
    /// </summary>
    /// <typeparam name="T">The type that implements ISerializable.</typeparam>
    /// <param name="key">The key identifier for the object in the serialization format.</param>
    /// <param name="value">The object to be serialized.</param>
    public override void BindSerializable<T>(string key, T value)
    {
        try
        {
            BinarySerializeWriteNode node = new BinarySerializeWriteNode(_referenceContext, OnError);
            _referenceContext?.TryWriteReferenceId(node, value);
            value.OnSerialize(node, SerializeMode.Save);
            _content.Add(key, node._content);
        }
        catch (Exception ex)
        {
            AddError($"Failed to bind serializable '{key}': {ex}");
        }
    }

    /// <summary>
    /// Binds a nullable complex object that implements ISerializable for serialization with error handling.
    /// Handles exceptions gracefully to prevent serialization errors from affecting other nodes.
    /// </summary>
    /// <typeparam name="T">The type that implements ISerializable.</typeparam>
    /// <param name="key">The key identifier for the object in the serialization format.</param>
    /// <param name="value">Reference to the nullable object to be serialized.</param>
    /// <param name="onCreate">Factory function (not used during serialization).</param>
    public override void BindSerializableOptional<T>(string key, ref T? value, Func<SerializeReadNode, T> onCreate) where T : default
    {
        try
        {
            if (value == null)
            {
                _content.Add(key, new BinaryValue());
            }
            else
            {
                BinarySerializeWriteNode node = new BinarySerializeWriteNode(_referenceContext, OnError);
                _referenceContext?.TryWriteReferenceId(node, value);
                value.OnSerialize(node, SerializeMode.Save);
                _content.Add(key, node._content);
            }
        }
        catch (Exception ex)
        {
            AddError($"Failed to bind optional serializable '{key}': {ex}");
        }
    }

    public override void BindMemory<T>(string key, Span<T> memory)
    {
        _content.Add(key, BinaryValue.CreateByMemory(memory));
    }

    public override void BindCollection<T>(string key, ICollection<T> value)
    {
        BinaryArray array = new BinaryArray();
        foreach (var item in value)
        {
            array.Add(BinaryValue.CreateByValue(item));
        }

        _content.Add(key, array);
    }

    public override void BindCollection(string key, ICollection<string> value)
    {
        BinaryArray array = new BinaryArray();
        foreach (var item in value)
        {
            array.Add(item);
        }

        _content.Add(key, array);
    }

    public override void BindArraySerializable<T>(string key, IReadOnlyList<T> value)
    {
        BinaryArray array = new BinaryArray();
        for (int i = 0; i < value.Count; i++)
        {
            try
            {
                BinarySerializeWriteNode node = new BinarySerializeWriteNode(_referenceContext, OnError);
                _referenceContext?.TryWriteReferenceId(node, value[i]);
                value[i].OnSerialize(node, SerializeMode.Save);
                array.Add(node._content);
            }
            catch (Exception ex)
            {
                AddError($"Failed to bind array serializable '{key}': {ex}");
            }
        }
        _content.Add(key, array);
    }

    /// <summary>
    /// Binds a collection of complex objects that implement ISerializable for serialization.
    /// Handles exceptions gracefully per list item to prevent individual errors from affecting the entire collection.
    /// </summary>
    /// <typeparam name="T">The type that implements ISerializable and has a parameterless constructor.</typeparam>
    /// <param name="key">The key identifier for the collection in the serialization format.</param>
    /// <param name="value">The collection of serializable objects to be serialized.</param>
    public override void BindCollectionSerializable<T>(string key, ICollection<T> value)
    {
        BinaryArray array = new BinaryArray();
        int i = 0;
        foreach (var element in value)
        {
            try
            {
                BinarySerializeWriteNode node = new BinarySerializeWriteNode(_referenceContext, OnError);
                _referenceContext?.TryWriteReferenceId(node, element);
                element.OnSerialize(node, SerializeMode.Save);
                array.Add(node._content);
            }
            catch (Exception ex)
            {
                AddError($"Failed to bind serializable list item at index {i} for key '{key}': {ex}");
            }
            i++;
        }

        _content.Add(key, array);
    }

    /// <summary>
    /// Binds a collection of complex objects that implement ISerializable for serialization with custom factory.
    /// Handles exceptions gracefully per list item to prevent individual errors from affecting the entire collection.
    /// </summary>
    /// <typeparam name="T">The type that implements ISerializable.</typeparam>
    /// <param name="key">The key identifier for the collection in the serialization format.</param>
    /// <param name="value">The collection of serializable objects to be serialized.</param>
    /// <param name="onCreate">Factory function (not used during serialization).</param>
    public override void BindCollectionSerializable<T>(string key, ICollection<T> value, Func<SerializeReadNode, T> onCreate)
    {
        BinaryArray array = new BinaryArray();
        int i = 0;
        foreach (var element in value)
        {
            try
            {
                BinarySerializeWriteNode node = new BinarySerializeWriteNode(_referenceContext, OnError);
                _referenceContext?.TryWriteReferenceId(node, element);
                element.OnSerialize(node, SerializeMode.Save);
                array.Add(node._content);
            }
            catch (Exception ex)
            {
                AddError($"Failed to bind serializable list item at index {i} for key '{key}': {ex}");
            }
            i++;
        }

        _content.Add(key, array);
    }

    public override void BindDictionarySerializable<T>(string key, IDictionary<string, T> value)
    {
        BinaryTable table = new BinaryTable();
        foreach (var item in value)
        {
            try
            {
                BinarySerializeWriteNode node = new BinarySerializeWriteNode(_referenceContext, OnError);
                _referenceContext?.TryWriteReferenceId(node, item.Value);
                item.Value.OnSerialize(node, SerializeMode.Save);
                table.Add(item.Key, node._content);
            }
            catch (Exception ex)
            {
                AddError($"Failed to bind serializable dictionary item key '{item.Key}' for key '{key}': {ex}");
            }
        }
        _content.Add(key, table);
    }

    public override void BindDictionarySerializable<T>(string key, IDictionary<string, T> value, Func<SerializeReadNode, T> onCreate)
    {
        BinaryTable table = new BinaryTable();
        foreach (var item in value)
        {
            try
            {
                BinarySerializeWriteNode node = new BinarySerializeWriteNode(_referenceContext, OnError);
                _referenceContext?.TryWriteReferenceId(node, item.Value);
                item.Value.OnSerialize(node, SerializeMode.Save);
                table.Add(item.Key, node._content);
            }
            catch (Exception ex)
            {
                AddError($"Failed to bind serializable dictionary item key '{item.Key}' for key '{key}': {ex}");
            }
        }
        _content.Add(key, table);
    }

    public override void SetValue<T>(string key, T value)
    {
        _content.Add(key, BinaryValue.CreateByValue(value));
    }

    public override void SetEnum<T>(string key, T value)
    {
        _content.Add(key, BinaryValue.CreateByEnum(value));
    }

    public override void SetString(string key, string value)
    {
        _content.Add(key, value);
    }

    public override void BindDictionary<TValue>(string key, IDictionary<string, TValue> value)
    {
        BinaryTable table = new BinaryTable();
        foreach (var item in value)
        {
            table.Add(item.Key, BinaryValue.CreateByValue(item.Value));
        }
        _content.Add(key, table);
    }

    public override void BindDictionary(string key, IDictionary<string, string> value)
    {
        BinaryTable table = new BinaryTable();
        foreach (var item in value)
        {
            table.Add(item.Key, item.Value);
        }
        _content.Add(key, table);
    }

    public override void BindDictionary(string key, IDictionary<string, ReadOnlyMemory<byte>> value)
    {
        BinaryTable table = new BinaryTable();
        foreach (var item in value)
        {
            table.Add(item.Key, new BinaryValue(item.Value));
        }
        _content.Add(key, table);
    }

    public override void BindBinary(string key, ref ReadOnlyMemory<byte> data)
    {
        _content.Add(key, new BinaryValue(data));
    }

    public override void BindReference<T>(string key, ref T? referenceable) where T : default
    {
        if(_referenceContext == null)
        {
            return;
        }

        if (referenceable == null)
        {
            return;
        }

        uint id = _referenceContext.GetId(referenceable);
        _content.Add(key, id);
    }



}