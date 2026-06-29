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
    private readonly bool _clearMissingReferences;
    protected BinaryTable _content;
    public BinaryTable Content => _content;

    public override ReferenceContext? ReferenceContext => _referenceContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="BinaryPostLoadSerializeNode"/> class.
    /// </summary>
    /// <param name="referenceContext">The context used to resolve object references.</param>
    /// <param name="content">The serialized content traversed during post-load.</param>
    /// <param name="onError">Optional error callback.</param>
    /// <param name="clearMissingReferences">
    /// Whether a missing reference ID clears the existing destination before resolution.
    /// Use this when populating a reused object from authoritative serialized state.
    /// </param>
    public BinaryPostLoadSerializeNode(
        ReferenceContext? referenceContext,
        BinaryTable content,
        Action<string>? onError = null,
        bool clearMissingReferences = false)
    {
        _referenceContext = referenceContext;
        _content = content;
        OnError = onError;
        _clearMissingReferences = clearMissingReferences;
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
                BinaryPostLoadSerializeNode node = new BinaryPostLoadSerializeNode(
                    _referenceContext, table, OnError, _clearMissingReferences);
                value.OnSerialize(node, SerializeMode.PostLoad);
                return;
            }

            if (_clearMissingReferences)
            {
                BinaryPostLoadSerializeNode node = new BinaryPostLoadSerializeNode(
                    _referenceContext, new BinaryTable(), OnError, clearMissingReferences: true);
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
            if (value is null)
                return;

            if (_content.TryGetTable(key, out BinaryTable? table))
            {
                BinaryPostLoadSerializeNode node = new BinaryPostLoadSerializeNode(
                    _referenceContext, table, OnError, _clearMissingReferences);
                value.OnSerialize(node, SerializeMode.PostLoad);
                return;
            }

            if (_clearMissingReferences)
                value = default;
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
                        BinaryPostLoadSerializeNode node = new BinaryPostLoadSerializeNode(
                            _referenceContext, table, OnError, _clearMissingReferences);
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
                        BinaryPostLoadSerializeNode node = new BinaryPostLoadSerializeNode(
                            _referenceContext, table, OnError, _clearMissingReferences);
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
                        BinaryPostLoadSerializeNode node = new BinaryPostLoadSerializeNode(
                            _referenceContext, table, OnError, _clearMissingReferences);
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

    public override void BindDictionarySerializable<T>(string key, IDictionary<string, T> value)
    {
        if (_content.TryGetTable(key, out BinaryTable? table))
        {
            foreach (var item in value)
            {
                try
                {
                    if (table.TryGetTable(item.Key, out BinaryTable? itemTable))
                    {
                        BinaryPostLoadSerializeNode node = new BinaryPostLoadSerializeNode(
                            _referenceContext, itemTable, OnError, _clearMissingReferences);
                        item.Value.OnSerialize(node, SerializeMode.PostLoad);
                    }
                }
                catch (Exception ex)
                {
                    AddError($"Failed to post-load dictionary item key '{item.Key}' for key '{key}': {ex}");
                }
            }
        }
    }

    public override void BindDictionarySerializable<T>(string key, IDictionary<string, T> value, Func<SerializeReadNode, T> onCreate)
    {
        if (_content.TryGetTable(key, out BinaryTable? table))
        {
            foreach (var item in value)
            {
                try
                {
                    if (table.TryGetTable(item.Key, out BinaryTable? itemTable))
                    {
                        BinaryPostLoadSerializeNode node = new BinaryPostLoadSerializeNode(
                            _referenceContext, itemTable, OnError, _clearMissingReferences);
                        item.Value.OnSerialize(node, SerializeMode.PostLoad);
                    }
                }
                catch (Exception ex)
                {
                    AddError($"Failed to post-load dictionary item key '{item.Key}' for key '{key}': {ex}");
                }
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
        if (_referenceContext == null)
        {
            return;
        }

        if (_clearMissingReferences)
            referenceable = default;

        if (!TryGetId(key, out uint id) || id == 0)
            return;

        if (_referenceContext.TryGetReference(id, out object? obj) && obj is T reference)
        {
            referenceable = reference;
            return;
        }

        AddError($"Failed to resolve reference '{key}': {id}\n{Environment.StackTrace}");
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
