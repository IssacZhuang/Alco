using System;
using System.Collections.Generic;

namespace Alco;

/// <summary>
/// A serialization node used during the post-load phase.
/// This node does not read or write any data. It only traverses objects
/// and invokes <see cref="ISerializable.OnSerialize"/> with <see cref="SerializeMode.PostLoad"/>.
/// </summary>
public class BinaryPostLoadSerializeNode : SerializeNode
{
    private readonly ReferenceContext? _referenceContext;
    protected BinaryTable _content;
    public BinaryTable Content => _content;

    public override ReferenceContext? ReferenceContext => _referenceContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="BinaryPostLoadSerializeNode"/> class.
    /// </summary>
    /// <param name="onError">Optional error callback.</param>
    public BinaryPostLoadSerializeNode(ReferenceContext? referenceContext, BinaryTable content, Action<string>? onError = null)
    {
        _referenceContext = referenceContext;
        _content = content;
        OnError = onError;

    }

    /// <summary>
    /// No-op for post-load: values are not read or written.
    /// </summary>
    public override void BindValue<T>(string key, ref T value, T @default = default)
    {
        // Intentionally empty: no serialization during post-load
    }

    /// <summary>
    /// No-op for post-load: enums are not read or written.
    /// </summary>
    public override void BindEnum<T>(string key, ref T value, T @default = default)
    {
        // Intentionally empty: no serialization during post-load
    }

    /// <summary>
    /// No-op for post-load: strings are not read or written.
    /// </summary>
    public override void BindString(string key, ref string value, string @default = "")
    {
        // Intentionally empty: no serialization during post-load
    }

    /// <summary>
    /// Invokes post-load serialization on the provided serializable value.
    /// </summary>
    public override void BindSerializable<T>(string key, T value)
    {
        try
        {
            if (_content.TryGetTable(key, out BinaryTable? table))
            {
                BinaryPostLoadSerializeNode node = new BinaryPostLoadSerializeNode(_referenceContext, table, OnError);
                value.OnSerialize(node, SerializeMode.PostLoad);
            }
        }
        catch (Exception ex)
        {
            AddError($"Failed to post-load '{key}': {ex}");
        }
    }

    /// <summary>
    /// Invokes post-load serialization on an optional serializable value if present.
    /// </summary>
    public override void BindSerializableOptional<T>(string key, ref T? value, Func<SerializeReadNode, T> onCreate) where T : default
    {
        try
        {
            if (_content.TryGetTable(key, out BinaryTable? table) && value is not null)
            {
                BinaryPostLoadSerializeNode node = new BinaryPostLoadSerializeNode(_referenceContext, table, OnError);
                value.OnSerialize(node, SerializeMode.PostLoad);
            }
        }
        catch (Exception ex)
        {
            AddError($"Failed to post-load optional '{key}': {ex}");
        }
    }

    /// <summary>
    /// No-op for post-load: raw memory is not processed.
    /// </summary>
    public override void BindMemory<T>(string key, Span<T> memory)
    {
        // Intentionally empty
    }

    /// <summary>
    /// No-op for post-load: primitive collections are not processed.
    /// </summary>
    public override void BindCollection<T>(string key, ICollection<T> value)
    {
        // Intentionally empty
    }

    /// <summary>
    /// No-op for post-load: string collections are not processed.
    /// </summary>
    public override void BindCollection(string key, ICollection<string> value)
    {
        // Intentionally empty
    }

    public override void BindArraySerializable<T>(string key, IReadOnlyList<T> value)
    {
        if (_content.TryGetArray(key, out BinaryArray? array))
        {
            int length = Math.Min(array.Count, value.Count);
            for (int i = 0; i < length; i++)
            {
                try
                {
                    if (array.TryGetTable(i, out BinaryTable? table))
                    {
                        BinaryPostLoadSerializeNode node = new BinaryPostLoadSerializeNode(_referenceContext, table, OnError);
                        value[i].OnSerialize(node, SerializeMode.PostLoad);
                    }
                }
                catch (Exception ex)
                {
                    AddError($"Failed to post-load array serializable item at index {i} for key '{key}': {ex}");
                }
            }
        }
    }

    /// <summary>
    /// Traverses and invokes post-load on each serializable item in the collection.
    /// </summary>
    public override void BindCollectionSerializable<T>(string key, ICollection<T> value)
    {
        if (_content.TryGetArray(key, out BinaryArray? array))
        {
            int index = 0;
            foreach (T item in value)
            {
                try
                {
                    if (index < array.Count && array.TryGetTable(index, out BinaryTable? table))
                    {
                        BinaryPostLoadSerializeNode node = new BinaryPostLoadSerializeNode(_referenceContext, table, OnError);
                        item.OnSerialize(node, SerializeMode.PostLoad);
                    }
                }
                catch (Exception ex)
                {
                    AddError($"Failed to post-load list item at index {index} for key '{key}': {ex}");
                }
                index++;
            }
        }
    }

    /// <summary>
    /// Traverses and invokes post-load on each serializable item in the collection.
    /// </summary>
    public override void BindCollectionSerializable<T>(string key, ICollection<T> value, Func<SerializeReadNode, T> onCreate)
    {
        if (_content.TryGetArray(key, out BinaryArray? array))
        {
            int index = 0;
            foreach (T item in value)
            {
                try
                {
                    if (index < array.Count && array.TryGetTable(index, out BinaryTable? table))
                    {
                        BinaryPostLoadSerializeNode node = new BinaryPostLoadSerializeNode(_referenceContext, table, OnError);
                        item.OnSerialize(node, SerializeMode.PostLoad);
                    }
                }
                catch (Exception ex)
                {
                    AddError($"Failed to post-load list item at index {index} for key '{key}': {ex}");
                }
                index++;
            }
        }
    }

    /// <summary>
    /// No-op for post-load: dictionaries of primitive or binary data are not processed.
    /// </summary>
    public override void BindDictionary<TValue>(string key, IDictionary<string, TValue> value)
    {
        // Intentionally empty
    }

    /// <summary>
    /// No-op for post-load: dictionaries of primitive or binary data are not processed.
    /// </summary>
    public override void BindDictionary(string key, IDictionary<string, string> value)
    {
        // Intentionally empty
    }

    /// <summary>
    /// No-op for post-load: dictionaries of primitive or binary data are not processed.
    /// </summary>
    public override void BindDictionary(string key, IDictionary<string, ReadOnlyMemory<byte>> value)
    {
        // Intentionally empty
    }

    /// <summary>
    /// No-op for post-load: binary data is not processed.
    /// </summary>
    public override void BindBinary(string key, ref ReadOnlyMemory<byte> data)
    {
        // Intentionally empty
    }

    public override void BindReference<T>(string key, ref T? referenceable) where T : default
    {
        if(_referenceContext == null)
        {
            return;
        }

        if (TryGetId(key, out uint id) && _referenceContext.TryGetReference(id, out object? obj) && obj is T reference)
        {
            referenceable = reference;
        }
        else
        {
            AddError($"Failed to resolve reference '{key}': {id}");
        }
    }

    private bool TryGetId(string key, out uint id)
    {
        if (_content.TryGetValue(key, out uint v))
        {
            id = v;
            return true;
        }
        else
        {
            id = default;
            return false;
        }
    }


}


