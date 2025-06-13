using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Alco;

public class BinarySerializeWriteNode : SerializeWriteNode
{
    protected BinaryTable _content = new BinaryTable();
    public BinaryTable Content => _content;

    public BinarySerializeWriteNode(Action<string>? onError = null)
    {
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
            BinarySerializeWriteNode node = new BinarySerializeWriteNode(OnError);
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
                BinarySerializeWriteNode node = new BinarySerializeWriteNode(OnError);
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

    public override void BindList<T>(string key, IList<T> value)
    {
        BinaryArray array = new BinaryArray();
        for (int i = 0; i < value.Count; i++)
        {
            array.Add(BinaryValue.CreateByValue(value[i]));
        }

        _content.Add(key, array);
    }

    public override void BindList(string key, IList<string> value)
    {
        BinaryArray array = new BinaryArray();
        for (int i = 0; i < value.Count; i++)
        {
            array.Add(value[i]);
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
    public override void BindListSerializable<T>(string key, IList<T> value)
    {
        BinaryArray array = new BinaryArray();
        for (int i = 0; i < value.Count; i++)
        {
            try
            {
                BinarySerializeWriteNode node = new BinarySerializeWriteNode(OnError);
                value[i].OnSerialize(node, SerializeMode.Save);
                array.Add(node._content);
            }
            catch (Exception ex)
            {
                AddError($"Failed to bind serializable list item at index {i} for key '{key}': {ex}");
            }
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
    public override void BindListSerializable<T>(string key, IList<T> value, Func<SerializeReadNode, T> onCreate)
    {
        BinaryArray array = new BinaryArray();
        for (int i = 0; i < value.Count; i++)
        {
            try
            {
                BinarySerializeWriteNode node = new BinarySerializeWriteNode(OnError);
                value[i].OnSerialize(node, SerializeMode.Save);
                array.Add(node._content);
            }
            catch (Exception ex)
            {
                AddError($"Failed to bind serializable list item at index {i} for key '{key}': {ex}");
            }
        }

        _content.Add(key, array);
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

    public override void BindDictionary(string key, IDictionary<string, byte[]> value)
    {
        BinaryTable table = new BinaryTable();
        foreach (var item in value)
        {
            table.Add(item.Key, BinaryValue.CreateByMemory(item.Value.AsSpan()));
        }
        _content.Add(key, table);
    }
}