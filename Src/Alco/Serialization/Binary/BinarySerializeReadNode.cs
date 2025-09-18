using System;
using System.Collections;
using System.Collections.Generic;

namespace Alco;

public class BinarySerializeReadNode : SerializeReadNode
{
    private readonly ReferenceContext _referenceContext;
    protected BinaryTable _content;
    public BinaryTable Content => _content;
    public BinarySerializeReadNode(ReferenceContext referenceContext, BinaryTable content, Action<string>? onError = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        _content = content;
        _referenceContext = referenceContext;
        OnError = onError;
    }

    /// <summary>
    /// Binds a complex object that implements ISerializable for deserialization.
    /// Handles exceptions gracefully to prevent serialization errors from affecting other nodes.
    /// </summary>
    /// <typeparam name="T">The type that implements ISerializable.</typeparam>
    /// <param name="key">The key identifier for the object in the serialization format.</param>
    /// <param name="value">The existing object to be deserialized into.</param>
    public override void BindSerializable<T>(string key, T value)
    {
        try
        {
            if (_content.TryGetTable(key, out BinaryTable? table))
            {
                BinarySerializeReadNode node = new BinarySerializeReadNode(_referenceContext, table, OnError);
                value.OnSerialize(node, SerializeMode.Load);
                TryWriteReferenceId(node, value);
            }
        }
        catch (Exception ex)
        {
            AddError($"Failed to bind serializable '{key}': {ex}");
        }
    }

    /// <summary>
    /// Binds a nullable complex object that implements ISerializable for deserialization with error handling.
    /// Handles exceptions gracefully to prevent serialization errors from affecting other nodes.
    /// </summary>
    /// <typeparam name="T">The type that implements ISerializable.</typeparam>
    /// <param name="key">The key identifier for the object in the serialization format.</param>
    /// <param name="value">Reference to the nullable object to be deserialized.</param>
    /// <param name="onCreate">Factory function to create a new instance during deserialization.</param>
    public override void BindSerializableOptional<T>(string key, ref T? value, Func<SerializeReadNode, T> onCreate) where T : default
    {
        try
        {
            if (_content.TryGetTable(key, out BinaryTable? table))
            {
                BinarySerializeReadNode node = new BinarySerializeReadNode(_referenceContext, table, OnError);
                value ??= onCreate(node);
                value.OnSerialize(node, SerializeMode.Load);
                TryWriteReferenceId(node, value);
            }
        }
        catch (Exception ex)
        {
            AddError($"Failed to bind optional serializable '{key}': {ex}");
        }
    }

    public unsafe override void BindMemory<T>(string key, Span<T> memory)
    {
        if (_content.TryGetBinary(key, out byte[]? binaryValue))
        {
            int length = Math.Min(memory.Length * sizeof(T), binaryValue.Length);
            fixed (T* ptrMemory = memory)
            {
                Span<byte> span = new Span<byte>(ptrMemory, length);
                binaryValue.AsSpan().CopyTo(span);
            }
        }
    }

    public override void BindCollection<T>(string key, ICollection<T> value)
    {
        value.Clear();
        if (_content.TryGetArray(key, out BinaryArray? array))
        {
            for (int i = 0; i < array.Count; i++)
            {
                if (array.TryGetBinary(i, out byte[]? binaryValue))
                {
                    value.Add(UtilsBinary.DecodeToValue<T>(binaryValue));
                }
            }
        }
    }

    public override void BindCollection(string key, ICollection<string> value)
    {
        value.Clear();
        if (_content.TryGetArray(key, out BinaryArray? array))
        {
            for (int i = 0; i < array.Count; i++)
            {
                if (array.TryGetString(i, out string? stringValue))
                {
                    value.Add(stringValue);
                }
            }
        }
    }

    /// <summary>
    /// Binds a collection of complex objects that implement ISerializable for deserialization.
    /// Handles exceptions gracefully per list item to prevent individual errors from affecting the entire collection.
    /// </summary>
    /// <typeparam name="T">The type that implements ISerializable and has a parameterless constructor.</typeparam>
    /// <param name="key">The key identifier for the collection in the serialization format.</param>
    /// <param name="value">The collection of serializable objects to be deserialized.</param>
    public override void BindCollectionSerializable<T>(string key, ICollection<T> value)
    {
        value.Clear();
        if (_content.TryGetArray(key, out BinaryArray? array))
        {

            int i = 0;
            try
            {
                for (i = 0; i < array.Count; i++)
                {
                    if (array.TryGetTable(i, out BinaryTable? table))
                    {
                        BinarySerializeReadNode node = new BinarySerializeReadNode(_referenceContext, table, OnError);
                        T item = new();
                        item.OnSerialize(node, SerializeMode.Load);
                        TryWriteReferenceId(node, item);
                        value.Add(item);
                    }
                }
            }
            catch (Exception ex)
            {
                AddError($"Failed to bind serializable list item at index {i} for key '{key}': {ex}");
            }
        }
    }

    /// <summary>
    /// Binds a collection of complex objects that implement ISerializable for deserialization with custom factory.
    /// Handles exceptions gracefully per list item to prevent individual errors from affecting the entire collection.
    /// </summary>
    /// <typeparam name="T">The type that implements ISerializable.</typeparam>
    /// <param name="key">The key identifier for the collection in the serialization format.</param>
    /// <param name="value">The collection of serializable objects to be deserialized.</param>
    /// <param name="onCreate">Factory function to create a new instance during deserialization.</param>
    public override void BindCollectionSerializable<T>(string key, ICollection<T> value, Func<SerializeReadNode, T> onCreate)
    {
        value.Clear();
        if (_content.TryGetArray(key, out BinaryArray? array))
        {
            int i = 0;
            try
            {
                for (i = 0; i < array.Count; i++)
                {       
                    if (array.TryGetTable(i, out BinaryTable? table))
                    {
                        BinarySerializeReadNode node = new BinarySerializeReadNode(_referenceContext, table, OnError);
                        T item = onCreate(node);
                        item.OnSerialize(node, SerializeMode.Load);
                        TryWriteReferenceId(node, item);
                        value.Add(item);
                    }
                }
            }
            catch (Exception ex)
            {
                AddError($"Failed to bind serializable list item at index {i} for key '{key}': {ex}");
            }
        }
    }

    public override T GetValue<T>(string key, T @default = default)
    {
        if (_content.TryGetValue(key, out T v))
        {
            return v;
        }
        else
        {
            return @default;
        }
    }

    public override T GetEnum<T>(string key, T @default = default)
    {
        if (_content.TryGetEnum(key, out T v))
        {
            return v;
        }
        else
        {
            return @default;
        }
    }

    public override string GetString(string key, string @default = "")
    {
        if (_content.TryGetString(key, out string? stringValue))
        {
            return stringValue;
        }
        else
        {
            return @default;
        }
    }

    public override void BindDictionary<TValue>(string key, IDictionary<string, TValue> value)
    {
        value.Clear();
        if (_content.TryGetTable(key, out BinaryTable? table))
        {
            foreach (var itemKey in table.Keys)
            {
                if (table.TryGetBinary(itemKey, out byte[]? binaryValue))
                {
                    value.Add(itemKey, UtilsBinary.DecodeToValue<TValue>(binaryValue));
                }
            }
        }
    }

    public override void BindDictionary(string key, IDictionary<string, string> value)
    {
        value.Clear();
        if (_content.TryGetTable(key, out BinaryTable? table))
        {
            foreach (var itemKey in table.Keys)
            {
                if (table.TryGetString(itemKey, out string? stringValue))
                {
                    value.Add(itemKey, stringValue);
                }
            }
        }
    }

    public override void BindDictionary(string key, IDictionary<string, byte[]> value)
    {
        value.Clear();
        if (_content.TryGetTable(key, out BinaryTable? table))
        {
            foreach (var itemKey in table.Keys)
            {
                if (table.TryGetBinary(itemKey, out byte[]? binaryValue))
                {
                    value.Add(itemKey, binaryValue);
                }
            }
        }
    }

    public override void BindReference<T>(string key, ref T? referenceable) where T : default
    {
        //do nothing
    }

    private void TryWriteReferenceId(BinarySerializeReadNode node, ISerializable value)
    {
        if(value is IReferenceable referenceable)
        {
            uint id = node.GetValue<uint>(ReferenceContext.SerializeKey);
            _referenceContext.SetReference(id, referenceable);
        }
    }

    
}